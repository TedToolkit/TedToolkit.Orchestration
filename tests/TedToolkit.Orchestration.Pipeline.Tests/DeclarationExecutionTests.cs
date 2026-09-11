namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ArgumentFailureHappensBeforeAnyStepAttempt()
    {
        var result = await Run("""
            internal static class WorkStepMethods
            {
                [Step]
                internal static int Work(int value, CancellationToken token)
                {
                    Example.Constructions++;
                    return 42;
                }
            }
            public static partial class Example
            {
                public static int Evaluations;
                public static int Constructions;
                private static int Fail() { Evaluations++; throw new InvalidOperationException("argument"); }
                [Pipeline]
                public static void Configuration(StepGraph p) { p.Work(Fail()).WithRetry(2); }
            }
            """ + AsyncScenario("""
                var pipeline = new Example.ConfigurationPipeline();
                try { pipeline.Execute(); return "unexpected"; }
                catch (InvalidOperationException error) { return $"{error.Message}:{Example.Evaluations}:{Example.Constructions}"; }
                """));
        await Assert.That(result).IsEqualTo("argument:1:0");
    }

    [Test]
    public async Task ExplicitArgumentMarkersPreserveInnerConversions()
    {
        var result = await Run(NamedSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p) { var sum = p.Add(((StepArgument<int>)(int)40.9), 2); }
            }
            """ + AsyncScenario("""
                return (new Example.ConfigurationPipeline().Execute()).Sum.ToString();
                """));
        await Assert.That(result).IsEqualTo("42");
    }

    [Test]
    public async Task ExpressionsWaitForDependenciesAndDoNotRunDuringConstruction()
    {
        var result = await Run(NamedSteps + """
            internal static class WaitStepMethods
            {
                [Step]
                internal static Task<int> Wait(Task<int> pending, CancellationToken token) => pending;
            }
            public static partial class Example
            {
                public static int Calls;
                private static int Next() { Calls++; return 2; }
                [Pipeline]
                public static void Configuration(StepGraph p, Task<int> pending) { var wait = p.Wait(pending); var sum = p.Add(wait, Next()); }
            }
            """ + AsyncScenario("""
                var pipeline = new Example.ConfigurationPipeline();
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
            internal static class RetryStepMethods
            {
                [Step]
                internal static Task<int> Retry(int value, State state, CancellationToken token)
                {
                    state.LastValue = value;
                    return ++state.Attempts % 2 == 1 ? Task.FromException<int>(new InvalidOperationException()) : Task.FromResult(value);
                }
            }
            public static partial class Example
            {
                private static int Next(State state) => ++state.Evaluations;
                [Pipeline]
                public static void Configuration(StepGraph p, State state) { p.Retry(Next(state), state).WithRetry(1); }
            }
            """ + AsyncScenario("""
                var state = new State();
                var pipeline = new Example.ConfigurationPipeline();
                var before = state.Evaluations;
                await pipeline.METHOD(state);
                var first = state.LastValue;
                await pipeline.METHOD(state);
                return $"{before}:{first}:{state.LastValue}:{state.Evaluations}:{state.Attempts}";
                """.Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")));
        await Assert.That(result).IsEqualTo("0:1:2:2:4");
    }

    [Test]
    public async Task LocalInitializersKeepDeclarationOrder()
    {
        var result = await Run("""
            internal static class PairSteps { [Step] internal static int Pair(int a, int b, CancellationToken token) => a * 10 + b; }
            public static partial class Example
            {
                public static string Order = "";
                private static int Next(string name) { Order += name; return Order.Length; }
                [Pipeline]
                public static void Configuration(StepGraph p)
                {
                    var first = Next("a");
                    var second = Next("b");
                    var pair = p.Pair(second, first);
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var result = new Example.ConfigurationPipeline().Execute();
                return $"{Example.Order}:{result.Pair}";
                """));
        await Assert.That(result).IsEqualTo("ab:21");
    }
    [Test]
    public async Task NamedArgumentsKeepSourceEvaluationOrder()
    {
        var result = await Run("""
            internal static class PairSteps { [Step] internal static int Pair(int a, int b, CancellationToken token) => a * 10 + b; }
            public static partial class Example
            {
                public static string Order = "";
                private static int Next(string name) { Order += name; return Order.Length; }
                [Pipeline]
                public static void Configuration(StepGraph p) { var pair = p.Pair(b: Next("b"), a: Next("a")); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var result = new Example.ConfigurationPipeline().Execute();
                return $"{Example.Order}:{result.Pair}";
                """));
        await Assert.That(result).IsEqualTo("ba:21");
    }

    [Test]
    public async Task LocalValuesAreComputedOncePerStepAndPreserveNameof()
    {
        var result = await Run(NamedSteps + """
            internal static class TextSteps { [Step] internal static string Text(string value, CancellationToken token) => value; }
            public static partial class Example
            {
                public static int Calls;
                private static int Next() => ++Calls;
                [Pipeline]
                public static void Configuration(StepGraph p)
                {
                    var seed = Next();
                    var offset = seed + 1;
                    var sum = p.Add(seed, offset);
                    var text = p.Text(nameof(seed));
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var pipeline = new Example.ConfigurationPipeline();
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
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p) { var sum = p.Add(M.Abs(-4), new[] { 1, 2 }.Sum()); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                return (new Example.ConfigurationPipeline().Execute()).Sum.ToString();
                """));
        await Assert.That(result).IsEqualTo("7");
    }

    [Test]
    [Arguments("Console.WriteLine(\"would be lost\"); p.Add(1, 2);")]
    [Arguments("var value = 1; value++; p.Add(value, 2);")]
    [Arguments("int Next() => 1; p.Add(Next(), 2);")]
    public async Task ExecutableConfigurationStatementsAreRejected(string body)
    {
        var generated = await Generate(NamedSteps + "public static partial class Example { public static void Configuration(StepGraph p) { " + body + " } }");
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    public async Task GeneratedCodeHasNoConfigurationCallOrRuntimeCapture()
    {
        var generated = await Generate(NamedSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p) { var sum = p.Add(40, 2); }
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource.Contains("Configuration(") || generated.GeneratedSource.Contains("IConfiguration") ||
            generated.GeneratedSource.Contains("_capture") || generated.GeneratedSource.Contains("GetFixedValue")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains(
            "global::AddStepMethods.Add(40, 2, executionToken)")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("RunSum0(")).IsTrue();
    }
}


