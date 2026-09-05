namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ReferencedStepExtensionsDoNotConflictWithConsumerGeneration()
    {
        var library = await Generate("""
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SharedPipelineConsumer")]
            internal readonly ref struct Foreign(int value) : IStep<int>
            {
                public int Execute(CancellationToken token) => value;
            }
            """, assemblyName: "SharedStepLibrary");
        await NoErrors(library);
        using var image = new MemoryStream();
        var emitted = library.Compilation.Emit(image);
        await Assert.That(emitted.Success).IsTrue();
        var consumer = await Generate("""
            public sealed partial class Consumer : Pipeline
            {
                protected override void Configuration(Pipeline.Builder p) { var foreign = p.Foreign(42); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                return (new Consumer(services).Execute()).Foreign.ToString();
                """), Microsoft.CodeAnalysis.MetadataReference.CreateFromImage(image.ToArray()), "SharedPipelineConsumer");
        await NoErrors(consumer);
    }

    [Test]
    public async Task SharedBuilderExtensionsKeepPipelineCapturesSeparate()
    {
        var result = await Run(NamedSteps + """
            public sealed partial class First : Pipeline
            {
                protected override void Configuration(Builder p) { var sum = p.Add(b: 2); }
            }
            public sealed partial class Second : Pipeline
            {
                protected override void Configuration(Builder p) { var sum = p.Add(a: 40); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var first = new First(services);
                var second = new Second(services);
                return $"{(first.Execute(sumA: 10)).Sum}:{(second.Execute(sumB: 2)).Sum}:{typeof(First.Builder) == typeof(Second.Builder)}";
                """));
        await Assert.That(result).IsEqualTo("12:42:True");
    }

    [Test]
    public async Task StepExtensionsAreGeneratedEvenBeforeAPipelineUsesThem()
    {
        var generated = await Generate(NamedSteps);
        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource.Contains(" Add(")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("this global::TedToolkit.Orchestration.Pipeline.Pipeline.Builder")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("struct Builder")).IsFalse();
    }

    [Test]
    public async Task CustomExtensionsCannotSubstituteTheAnalyzedStep()
    {
        var generated = await Generate(NamedSteps + """
            public static class Custom
            {
                public static StepBuilder<int> Add(this Pipeline.Builder builder, int a, int b) => default;
            }
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { var sum = p.Add(40, 2); }
            }
            """);
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    [Arguments("public partial class Example : Pipeline { protected override void Configuration(Builder p) { var a = p.Add(40, 2); } }")]
    [Arguments("public partial class Example : global::TedToolkit.Orchestration.Pipeline.Pipeline { protected virtual void Configure(Builder p) { var a = p.Add(40, 2); } }")]
    public async Task ReplaceableConfigurationIsRejected(string declaration)
    {
        var generated = await Generate(NamedSteps + declaration);
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    public async Task SealedConfigurationAllowsSafeFurtherInheritance()
    {
        var result = await Run(NamedSteps + """
            public partial class Example : Pipeline
            {
                protected sealed override void Configuration(Builder p) { var sum = p.Add(40, 2); }
            }
            public sealed class Derived : Example
            {
                public Derived(IServiceProvider services) : base(services) {}
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                return (new Derived(services).Execute()).Sum.ToString();
                """));
        await Assert.That(result).IsEqualTo("42");
    }

    [Test]
    [Arguments("public partial class Example : global::TedToolkit.Orchestration.Pipeline.Pipeline { private void Configuration(Builder p) {} }")]
    [Arguments("public partial class Example : Pipeline { protected override void Configuration(Builder p) {} private void Configure(Builder p) {} }")]
    public async Task InvalidConfigurationContractIsRejected(string declaration)
    {
        var generated = await Generate(declaration);
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    public async Task ArgumentExpressionsAreEvaluatedForEachInvocation()
    {
        var result = await Run(NamedSteps + """
            public sealed partial class Example : Pipeline
            {
                public static int Evaluations;
                private static int Next() => ++Evaluations;
                protected override void Configuration(Builder pipeline)
                {
                    var first = pipeline.Add(b: Next());
                    var second = pipeline.Add(first, Next());
                    var alias = second;
                    pipeline.Store(alias);
                }
            }
            """ + AsyncScenario("""
                var sink = new Sink();
                using var services = new ServiceCollection().AddSingleton(sink).BuildServiceProvider();
                var pipeline = new Example(services);
                var first = pipeline.Execute(firstA: 10);
                pipeline.ExecuteWithoutResults(firstA: 20);
                return $"{first.Second}:{sink.Value}:{Example.Evaluations}";
                """));
        await Assert.That(result).IsEqualTo("13:27:4");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InheritedPipelineAppliesTimeoutAndRetryBeforeDownstream(bool discard)
    {
        var result = await Run("""
            public sealed class State { public int Attempts; public int Cleanups; public int Stored; }
            [StepPolicy(RetryCount = 1, TimeoutMilliseconds = 30)]
            internal readonly ref struct Attempt([FromServices] State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => Run(state, token);
                private static async Task<int> Run(State state, CancellationToken token)
                {
                    try
                    {
                        if (++state.Attempts == 1) await Task.Delay(Timeout.Infinite, token);
                        if (state.Cleanups != 1) throw new InvalidOperationException("retry overlapped cleanup");
                        return 42;
                    }
                    finally { state.Cleanups++; }
                }
            }
            internal readonly ref struct Store(int value, [FromServices] State state) : IStep
            {
                public void Execute(CancellationToken token) => state.Stored = value;
            }
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder pipeline)
                {
                    var attempt = pipeline.Attempt();
                    pipeline.Store(attempt);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                await new Example(services).METHOD();
                return $"{state.Attempts}:{state.Cleanups}:{state.Stored}";
                """.Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")));
        await Assert.That(result).IsEqualTo("2:2:42");
    }

    [Test]
    public async Task GeneratedFlowNamesPoliciesAndOmitsRuntimeGraphIdentity()
    {
        var generated = await Generate("""
            [StepPolicy(RetryCount = 1, TimeoutMilliseconds = 100)]
            internal readonly ref struct Work : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(42);
            }
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder pipeline) { var work = pipeline.Work(); }
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource.Contains("new global::Work(")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("CancelAfter(")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains("retries0")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains("BeginNode") || generated.GeneratedSource.Contains("ValidateSource") ||
            generated.GeneratedSource.Contains("_configurationIndex") || generated.GeneratedSource.Contains("_capture")).IsFalse();
    }
}


