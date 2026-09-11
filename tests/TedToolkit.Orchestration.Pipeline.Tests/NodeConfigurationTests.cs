namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ConfigurationPolicyIsOwnedByTheNode()
    {
        var result = await Run("""
            public sealed class State { public int Attempts; }
            internal static class WorkStepMethods
            {
                [Step]
                internal static Task<int> Work([FromServices] State state, CancellationToken token) => Run(state, token);
                private static async Task<int> Run(State state, CancellationToken token)
                {
                    if (++state.Attempts == 1) throw new InvalidOperationException("retry");
                    await Task.Delay(50, token);
                    return 42;
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var work = pipeline.Work().WithRetry(1).WithTimeout(-1);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                var result = await new Example.ConfigurationPipeline(services).ExecuteAsync();
                return $"{state.Attempts}:{result.Work}";
                """));

        await Assert.That(result).IsEqualTo("2:42");
    }

    [Test]
    public async Task ZeroRetryMeansNoAdditionalAttempt()
    {
        var result = await Run("""
            public sealed class State { public int Attempts; }
            internal static class WorkStepMethods
            {
                [Step]
                internal static int Work([FromServices] State state, CancellationToken token)
                {
                    if (++state.Attempts == 1) throw new InvalidOperationException("failure");
                    return 42;
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var work = pipeline.Work().WithRetry(0);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                try { new Example.ConfigurationPipeline(services).Execute(); return "unexpected"; }
                catch (InvalidOperationException error) { return $"{error.Message}:{state.Attempts}"; }
                """));

        await Assert.That(result).IsEqualTo("failure:1");
    }

    [Test]
    public async Task ConfigurationTimeoutAppliesFromNodeModifier()
    {
        var result = await Run("""
            internal static class WaitStepMethods
            {
                [Step]
                internal static Task<int> Wait(CancellationToken token) => Run(token);
                private static async Task<int> Run(CancellationToken token)
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return 42;
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var wait = pipeline.Wait().WithTimeout(20);
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                try { await new Example.ConfigurationPipeline().ExecuteAsync(); return "unexpected"; }
                catch (TimeoutException) { return "timeout"; }
                """));

        await Assert.That(result).IsEqualTo("timeout");
    }

    [Test]
    public async Task DependsOnWaitsWithoutTransportingAResult()
    {
        var result = await Run("""
            public sealed class State
            {
                public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public bool DependentStarted;
            }
            internal static class PrepareStepMethods
            {
                [Step]
                internal static Task Prepare([FromServices] State state, CancellationToken token) => state.Release.Task;
            }
            internal static class ConsumeStepMethods
            {
                [Step]
                internal static int Consume([FromServices] State state, CancellationToken token)
                {
                    state.DependentStarted = true;
                    return 42;
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var prepare = pipeline.Prepare();
                    var consume = pipeline.Consume().DependsOn(prepare);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                var execution = new Example.ConfigurationPipeline(services).ExecuteAsync();
                var startedEarly = state.DependentStarted;
                state.Release.SetResult();
                var result = await execution;
                return $"{startedEarly}:{state.DependentStarted}:{result.Consume}";
                """));

        await Assert.That(result).IsEqualTo("False:True:42");
    }

    [Test]
    public async Task FailedControlDependencyPreventsDependentExecution()
    {
        var result = await Run("""
            public sealed class State { public bool DependentStarted; }
            internal static class PrepareStepMethods
            {
                [Step]
                internal static Task Prepare(CancellationToken token) => Task.FromException(new InvalidOperationException("prepare"));
            }
            internal static class ConsumeStepMethods
            {
                [Step]
                internal static void Consume([FromServices] State state, CancellationToken token) => state.DependentStarted = true;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var prepare = pipeline.Prepare();
                    pipeline.Consume().DependsOn(prepare);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                try { await new Example.ConfigurationPipeline(services).ExecuteAsync(); return "unexpected"; }
                catch (InvalidOperationException error) { return $"{error.Message}:{state.DependentStarted}"; }
                """));

        await Assert.That(result).IsEqualTo("prepare:False");
    }

    [Test]
    public async Task ControlDependencyWaitsWhileIndependentWorkRuns()
    {
        var result = await Run("""
            public sealed class State
            {
                public TaskCompletionSource<int> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource IndependentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public bool DependentStarted;
            }
            internal static class PrepareStepMethods
            {
                [Step]
                internal static Task<int> Prepare([FromServices] State state, CancellationToken token) => state.Release.Task;
            }
            internal static class IndependentStepMethods
            {
                [Step]
                internal static Task Independent([FromServices] State state, CancellationToken token)
                {
                    state.IndependentStarted.SetResult();
                    return Task.CompletedTask;
                }
            }
            internal static class ConsumeStepMethods
            {
                [Step]
                internal static void Consume([FromServices] State state, CancellationToken token) => state.DependentStarted = true;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var prepare = pipeline.Prepare();
                    pipeline.Independent();
                    pipeline.Consume().DependsOn(prepare);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                var execution = new Example.ConfigurationPipeline(services).ExecuteAsync();
                await state.IndependentStarted.Task;
                var startedEarly = state.DependentStarted;
                state.Release.SetResult(1);
                await execution;
                return $"{startedEarly}:{state.DependentStarted}";
                """));

        await Assert.That(result).IsEqualTo("False:True");
    }

    [Test]
    public async Task DependsOnChainPreservesCompletionOrderAcrossAllBuilderShapes()
    {
        var result = await Run("""
            public sealed class State
            {
                public global::System.Collections.Generic.List<string> Events { get; } = [];
                public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            internal static class FirstStepMethods
            {
                [Step]
                internal static Task<int> First([FromServices] State state, CancellationToken token) => Run(state);
                private static async Task<int> Run(State state)
                {
                    state.Events.Add("first-start");
                    state.Started.SetResult();
                    await state.Release.Task;
                    state.Events.Add("first-end");
                    return 1;
                }
            }
            internal static class SecondStepMethods
            {
                [Step]
                internal static void Second([FromServices] State state, CancellationToken token) => state.Events.Add("second");
            }
            internal static class ThirdStepMethods
            {
                [Step]
                internal static void Third([FromServices] State state, CancellationToken token) => state.Events.Add("third");
            }
            internal static class FourthStepMethods
            {
                [Step]
                internal static int Fourth([FromServices] State state, CancellationToken token)
                {
                    state.Events.Add("fourth");
                    return 4;
                }
            }
            internal static class FifthStepMethods
            {
                [Step]
                internal static int Fifth([FromServices] State state, CancellationToken token)
                {
                    state.Events.Add("fifth");
                    return 5;
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var first = pipeline.First();
                    var second = pipeline.Second().DependsOn(first);
                    var third = pipeline.Third().DependsOn(second);
                    var fourth = pipeline.Fourth().DependsOn(third);
                    var fifth = pipeline.Fifth().DependsOn(fourth);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                var execution = new Example.ConfigurationPipeline(services).ExecuteAsync();
                await state.Started.Task;
                var beforeRelease = string.Join(",", state.Events);
                state.Release.SetResult();
                var result = await execution;
                return $"{beforeRelease}|{string.Join(",", state.Events)}|{result.Fourth}:{result.Fifth}";
                """));

        await Assert.That(result).IsEqualTo(
            "first-start|first-start,first-end,second,third,fourth,fifth|4:5");
    }

    [Test]
    public async Task MultipleDependsOnEdgesWaitForEveryPrerequisite()
    {
        var result = await Run("""
            public sealed class State
            {
                public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource FirstCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public bool DependentStarted;
            }
            internal static class FirstStepMethods
            {
                [Step]
                internal static Task First([FromServices] State state, CancellationToken token) => Run(state);
                private static async Task Run(State state)
                {
                    state.FirstStarted.SetResult();
                    await state.ReleaseFirst.Task;
                    state.FirstCompleted.SetResult();
                }
            }
            internal static class SecondStepMethods
            {
                [Step]
                internal static Task<int> Second([FromServices] State state, CancellationToken token) => Run(state);
                private static async Task<int> Run(State state)
                {
                    state.SecondStarted.SetResult();
                    await state.ReleaseSecond.Task;
                    return 2;
                }
            }
            internal static class DependentStepMethods
            {
                [Step]
                internal static void Dependent([FromServices] State state, CancellationToken token) => state.DependentStarted = true;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var first = pipeline.First();
                    var second = pipeline.Second();
                    pipeline.Dependent().DependsOn(first).DependsOn(second);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                var execution = new Example.ConfigurationPipeline(services).ExecuteAsync();
                await Task.WhenAll(state.FirstStarted.Task, state.SecondStarted.Task);
                state.ReleaseFirst.SetResult();
                await state.FirstCompleted.Task;
                var startedAfterOne = state.DependentStarted;
                state.ReleaseSecond.SetResult();
                await execution;
                return $"{startedAfterOne}:{state.DependentStarted}";
                """));

        await Assert.That(result).IsEqualTo("False:True");
    }

    [Test]
    public async Task LoggerCategoryUsesConfiguredAndDefaultDisplayNames()
    {
        var result = await Run("""
            public sealed class CaptureLoggerFactory : ILoggerFactory
            {
                public static string Category = "";
                public ILogger CreateLogger(string categoryName)
                {
                    Category = categoryName;
                    return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                }
                public void AddProvider(ILoggerProvider provider) { }
                public void Dispose() { }
            }
            internal static class ReadNameStepMethods
            {
                [Step]
                internal static string ReadName(
                    [FromServices] ILogger logger,
                    CancellationToken token) => CaptureLoggerFactory.Category;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var automatic = pipeline.ReadName();
                    var configured = pipeline.ReadName().WithDisplayName("Readable name");
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection()
                    .AddSingleton<ILoggerFactory, CaptureLoggerFactory>()
                    .BuildServiceProvider();
                var result = new Example.ConfigurationPipeline(services).Execute();
                return $"{result.Automatic}:{result.Configured}";
                """));

        await Assert.That(result).IsEqualTo(
            "ReadNameStepMethods.ReadName[Example/automatic]:ReadNameStepMethods.ReadName[Example/Readable name]");
    }

    [Test]
    public async Task LoggerCategoryIsStableAcrossRetryAttempts()
    {
        var result = await Run("""
            public sealed class State { public global::System.Collections.Generic.List<string> Names { get; } = []; }
            public sealed class CaptureLoggerFactory : ILoggerFactory
            {
                public static string Category = "";
                public ILogger CreateLogger(string categoryName)
                {
                    Category = categoryName;
                    return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                }
                public void AddProvider(ILoggerProvider provider) { }
                public void Dispose() { }
            }
            internal static class ReadNameStepMethods
            {
                [Step]
                internal static int ReadName(
                    [FromServices] State state,
                    [FromServices] ILogger logger,
                    CancellationToken token)
                {
                    state.Names.Add(CaptureLoggerFactory.Category);
                    if (state.Names.Count == 1) throw new InvalidOperationException("retry");
                    return state.Names.Count;
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline)
                {
                    var configured = pipeline.ReadName().WithDisplayName("Stable").WithRetry(1);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                using var services = new ServiceCollection()
                    .AddSingleton(state)
                    .AddSingleton<ILoggerFactory, CaptureLoggerFactory>()
                    .BuildServiceProvider();
                var result = new Example.ConfigurationPipeline(services).Execute();
                return $"{string.Join(",", state.Names)}:{result.Configured}";
                """));

        await Assert.That(result).IsEqualTo(
            "ReadNameStepMethods.ReadName[Example/Stable],ReadNameStepMethods.ReadName[Example/Stable]:2");
    }

    [Test]
    [Arguments("pipeline.Work().WithRetry(-1);")]
    [Arguments("pipeline.Work().WithTimeout(0);")]
    [Arguments("pipeline.Work().WithDisplayName(\"\");")]
    [Arguments("pipeline.Work().WithRetry(1).WithRetry(2);")]
    [Arguments("pipeline.Work().WithTimeout(10).WithTimeout(20);")]
    [Arguments("pipeline.Work().WithDisplayName(\"first\").WithDisplayName(\"second\");")]
    public async Task InvalidOrDuplicateNodeModifiersAreRejected(string declaration)
    {
        var generated = await Generate("""
            internal static class WorkStepMethods
            {
                [Step]
                internal static void Work(CancellationToken token) { }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline) { DECLARATION }
            }
            """.Replace("DECLARATION", declaration));

        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    public async Task RuntimeComputedAndDetachedModifiersAreRejected()
    {
        var generated = await Generate("""
            internal static class WorkStepMethods
            {
                [Step]
                internal static void Work(CancellationToken token) { }
            }
            public static partial class Example
            {
                private static int RetryCount() => 1;
                public static void Configuration(StepGraph pipeline)
                {
                    var work = pipeline.Work();
                    var detached = work.WithTimeout(10);
                    pipeline.Work().WithRetry(RetryCount());
                }
            }
            """);

        await Assert.That(generated.Diagnostics.Count(item => item.Id == "TTP009")).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task NodeModifierOutsideConfigurationIsReported()
    {
        var generated = await Generate("""
            internal static class WorkStepMethods
            {
                [Step]
                internal static int Work(CancellationToken token) => 42;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline) { var work = pipeline.Work(); }
                public void Mutate(StepBuilder<int> work) { work.WithRetry(1); }
            }
            """);

        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP017")).IsTrue();
    }

    [Test]
    public async Task FunctionContainersMayOwnUnrelatedStaticMembers()
    {
        var generated = await Generate("""
            internal static class WorkStepMethods
            {
                public static int Value { get; set; }
                [Step]
                internal static void Work(CancellationToken token) { }
            }
            """);

        await NoErrors(generated);
    }
}
