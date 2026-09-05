namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ArgumentFailureHappensBeforeAnyStepAttempt()
    {
        var result = await Run("""
            [StepPolicy(RetryCount = 2)]
            internal readonly ref struct Work : IStep<int>
            {
                public Work(int value) { Example.Constructions++; }
                public int Execute(CancellationToken token) => 42;
            }
            public sealed partial class Example : Pipeline
            {
                public static int Evaluations;
                public static int Constructions;
                private static int Fail() { Evaluations++; throw new InvalidOperationException("argument"); }
                protected override void Configuration(Builder p) { p.Work(Fail()); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var pipeline = new Example(services);
                try { pipeline.Execute(); return "unexpected"; }
                catch (InvalidOperationException error) { return $"{error.Message}:{Example.Evaluations}:{Example.Constructions}"; }
                """));
        await Assert.That(result).IsEqualTo("argument:1:0");
    }

    [Test]
    public async Task ExplicitArgumentMarkersPreserveInnerConversions()
    {
        var result = await Run(NamedSteps + """
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { var sum = p.Add(((StepArgument<int>)(int)40.9), 2); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                return (new Example(services).Execute()).Sum.ToString();
                """));
        await Assert.That(result).IsEqualTo("42");
    }

    [Test]
    public async Task ExpressionsWaitForDependenciesAndDoNotRunDuringConstruction()
    {
        var result = await Run(NamedSteps + """
            internal readonly ref struct Wait(Task<int> pending) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => pending;
            }
            public sealed partial class Example : Pipeline
            {
                public static int Calls;
                private static int Next() { Calls++; return 2; }
                protected override void Configuration(Builder p) { var wait = p.Wait(); var sum = p.Add(wait, Next()); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var pipeline = new Example(services);
                var before = Example.Calls;
                var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = pipeline.ExecuteAsync(gate.Task);
                var waiting = Example.Calls;
                gate.SetResult(40);
                var first = await pending;
                var second = await pipeline.ExecuteAsync(Task.FromResult(8));
                return $"{before}:{waiting}:{first.Sum}:{second.Sum}:{Example.Calls}";
                """));
        await Assert.That(result).IsEqualTo("0:0:42:10:2");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRetriesReuseArgumentsButNextInvocationReevaluates(bool discard)
    {
        var result = await Run("""
            public sealed class State { public int Evaluations; public int Attempts; public int LastValue; }
            [StepPolicy(RetryCount = 1)]
            internal readonly ref struct Retry(int value, State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token)
                {
                    state.LastValue = value;
                    return ++state.Attempts % 2 == 1 ? Task.FromException<int>(new InvalidOperationException()) : Task.FromResult(value);
                }
            }
            public partial class Example : global::TedToolkit.Orchestration.Pipeline.Pipeline
            {
                private static int Next(State state) => ++state.Evaluations;
                private void Configure(Builder p, State state) { p.Retry(Next(state), state); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var state = new State();
                var pipeline = new Example(services, state);
                var before = state.Evaluations;
                await pipeline.METHOD();
                var first = state.LastValue;
                await pipeline.METHOD();
                return $"{before}:{first}:{state.LastValue}:{state.Evaluations}:{state.Attempts}";
                """.Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")));
        await Assert.That(result).IsEqualTo("0:1:2:2:4");
    }

    [Test]
    public async Task LocalInitializersKeepDeclarationOrder()
    {
        var result = await Run("""
            internal readonly ref struct Pair(int a, int b) : IStep<int> { public int Execute(CancellationToken token) => a * 10 + b; }
            public sealed partial class Example : Pipeline
            {
                public static string Order = "";
                private static int Next(string name) { Order += name; return Order.Length; }
                protected override void Configuration(Builder p)
                {
                    var first = Next("a");
                    var second = Next("b");
                    var pair = p.Pair(second, first);
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var result = new Example(services).Execute();
                return $"{Example.Order}:{result.Pair}";
                """));
        await Assert.That(result).IsEqualTo("ab:21");
    }
    [Test]
    public async Task NamedArgumentsKeepSourceEvaluationOrder()
    {
        var result = await Run("""
            internal readonly ref struct Pair(int a, int b) : IStep<int> { public int Execute(CancellationToken token) => a * 10 + b; }
            public sealed partial class Example : Pipeline
            {
                public static string Order = "";
                private static int Next(string name) { Order += name; return Order.Length; }
                protected override void Configuration(Builder p) { var pair = p.Pair(b: Next("b"), a: Next("a")); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var result = new Example(services).Execute();
                return $"{Example.Order}:{result.Pair}";
                """));
        await Assert.That(result).IsEqualTo("ba:21");
    }

    [Test]
    public async Task LocalValuesAreComputedOncePerStepAndPreserveNameof()
    {
        var result = await Run(NamedSteps + """
            internal readonly ref struct Text(string value) : IStep<string> { public string Execute(CancellationToken token) => value; }
            public sealed partial class Example : Pipeline
            {
                public static int Calls;
                private static int Next() => ++Calls;
                protected override void Configuration(Builder p)
                {
                    var seed = Next();
                    var offset = seed + 1;
                    var sum = p.Add(seed, offset);
                    var text = p.Text(nameof(seed));
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var pipeline = new Example(services);
                var first = pipeline.Execute();
                var second = pipeline.Execute();
                return $"{first.Sum}:{second.Sum}:{second.Text}:{Example.Calls}";
                """));
        await Assert.That(result).IsEqualTo("3:5:seed:2");
    }

    [Test]
    public async Task AliasesAndExtensionExpressionsRemainBound()
    {
        var result = await Run("using M = System.Math; using System.Linq;\n" + NamedSteps + """
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { var sum = p.Add(M.Abs(-4), new[] { 1, 2 }.Sum()); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                return (new Example(services).Execute()).Sum.ToString();
                """));
        await Assert.That(result).IsEqualTo("7");
    }

    [Test]
    [Arguments("Console.WriteLine(\"would be lost\"); p.Add(1, 2);")]
    [Arguments("var value = 1; value++; p.Add(value, 2);")]
    [Arguments("int Next() => 1; p.Add(Next(), 2);")]
    public async Task ExecutableConfigurationStatementsAreRejected(string body)
    {
        var generated = await Generate(NamedSteps + "public sealed partial class Example : Pipeline { protected override void Configuration(Builder p) { " + body + " } }");
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    public async Task GeneratedCodeHasNoConfigurationCallOrRuntimeCapture()
    {
        var generated = await Generate(NamedSteps + """
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { var sum = p.Add(40, 2); }
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource.Contains("Configuration(") || generated.GeneratedSource.Contains("IConfiguration") ||
            generated.GeneratedSource.Contains("_capture") || generated.GeneratedSource.Contains("GetFixedValue")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains("new global::Add(40, 2)")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("RunSum0(")).IsTrue();
    }
}


