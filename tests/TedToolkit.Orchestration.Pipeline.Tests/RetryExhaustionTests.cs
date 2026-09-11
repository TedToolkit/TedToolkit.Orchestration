namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task EveryStepShapePropagatesTheLastFailureAfterRetries(bool asynchronous, bool result)
    {
        var method = (asynchronous, result) switch
        {
            (false, false) => "internal static void Fail(State state, CancellationToken token)",
            (false, true) => "internal static int Fail(State state, CancellationToken token)",
            (true, false) => "internal static Task Fail(State state, CancellationToken token)",
            _ => "internal static Task<int> Fail(State state, CancellationToken token)"
        };
        var failure = asynchronous
            ? (result ? "return Task.FromException<int>(state.Last);" : "return Task.FromException(state.Last);")
            : "throw state.Last;";
        var execute = asynchronous
            ? (result ? "ExecuteAsync" : "ExecuteWithoutResultsAsync")
            : (result ? "Execute" : "ExecuteWithoutResults");

        var source = """
            public sealed class State
            {
                public int Attempts;
                public InvalidOperationException Last = null!;
            }
            internal static class FailStepMethods
            {
                [Step]
                METHOD
                {
                    state.Last = new InvalidOperationException($"attempt {++state.Attempts}");
                    FAILURE
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline, State state) { pipeline.Fail(state).WithRetry(2); } }
            """.Replace("METHOD", method).Replace("FAILURE", failure);

        var body = """
            using var services = new ServiceCollection().BuildServiceProvider();
            var state = new State();
            var pipeline = new Example.ConfigurationPipeline();
            try { INVOCATION; return SUCCESS; }
            catch (InvalidOperationException exception)
            {
                return RESULT;
            }
            """
            .Replace("INVOCATION", (asynchronous ? "await " : "") + "pipeline." + execute + "(state)")
            .Replace("SUCCESS", asynchronous ? "\"success\"" : "Task.FromResult(\"success\")")
            .Replace("RESULT", asynchronous
                ? "$\"{ReferenceEquals(exception, state.Last)}:{state.Attempts}:{exception.Message}\""
                : "Task.FromResult($\"{ReferenceEquals(exception, state.Last)}:{state.Attempts}:{exception.Message}\")");
        var scenario = asynchronous ? AsyncScenario(body) : Scenario(body);

        await Assert.That(await Run(source + scenario)).IsEqualTo("True:3:attempt 3");
    }

    [Test]
    public async Task ParallelRetryExhaustionCancelsAndDrainsStartedSiblings()
    {
        var result = await Run("""
            public sealed class State
            {
                public int Attempts;
                public int Cleaned;
                public InvalidOperationException Last = null!;
            }
            internal static class FailStepMethods
            {
                [Step]
                internal static Task<int> Fail(State state, CancellationToken token)
                {
                    state.Last = new InvalidOperationException($"attempt {++state.Attempts}");
                    return Task.FromException<int>(state.Last);
                }
            }
            internal static class WaitStepMethods
            {
                [Step]
                internal static Task<int> Wait(State state, CancellationToken token) => RunAsync(state, token);
                private static async Task<int> RunAsync(State state, CancellationToken token)
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return 1; }
                    finally { state.Cleaned++; }
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline, State state)
                {
                    var wait = pipeline.Wait(state);
                    var fail = pipeline.Fail(state).WithRetry(2);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                try { await new Example.ConfigurationPipeline().ExecuteAsync(state); return "success"; }
                catch (InvalidOperationException exception)
                {
                    return $"{ReferenceEquals(exception, state.Last)}:{state.Attempts}:{state.Cleaned}";
                }
                """));
        await Assert.That(result).IsEqualTo("True:3:1");
    }
}
