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
        var contract = (asynchronous, result) switch
        {
            (false, false) => "IStep",
            (false, true) => "IStep<int>",
            (true, false) => "IAsyncStep",
            _ => "IAsyncStep<int>"
        };
        var method = (asynchronous, result) switch
        {
            (false, false) => "public void Execute(CancellationToken token)",
            (false, true) => "public int Execute(CancellationToken token)",
            (true, false) => "public Task ExecuteAsync(CancellationToken token)",
            _ => "public Task<int> ExecuteAsync(CancellationToken token)"
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
            [StepPolicy(RetryCount = 2)]
            internal readonly ref struct Fail(State state) : CONTRACT
            {
                METHOD
                {
                    state.Last = new InvalidOperationException($"attempt {++state.Attempts}");
                    FAILURE
                }
            }
            public sealed partial class Example : Pipeline
            {
                private void Configure(Builder pipeline, State state) { pipeline.Fail(state); }
            }
            """.Replace("CONTRACT", contract).Replace("METHOD", method).Replace("FAILURE", failure);

        var body = """
            using var services = new ServiceCollection().BuildServiceProvider();
            var state = new State();
            var pipeline = new Example(services, state);
            try { INVOCATION; return SUCCESS; }
            catch (InvalidOperationException exception)
            {
                return RESULT;
            }
            """
            .Replace("INVOCATION", (asynchronous ? "await " : "") + "pipeline." + execute + "()")
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
            [StepPolicy(RetryCount = 2)]
            internal readonly ref struct Fail(State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token)
                {
                    state.Last = new InvalidOperationException($"attempt {++state.Attempts}");
                    return Task.FromException<int>(state.Last);
                }
            }
            internal readonly ref struct Wait(State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => RunAsync(state, token);
                private static async Task<int> RunAsync(State state, CancellationToken token)
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return 1; }
                    finally { state.Cleaned++; }
                }
            }
            public sealed partial class Example : Pipeline
            {
                private void Configure(Builder pipeline, State state)
                {
                    var wait = pipeline.Wait(state);
                    var fail = pipeline.Fail(state);
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var state = new State();
                try { await new Example(services, state).ExecuteAsync(); return "success"; }
                catch (InvalidOperationException exception)
                {
                    return $"{ReferenceEquals(exception, state.Last)}:{state.Attempts}:{state.Cleaned}";
                }
                """));
        await Assert.That(result).IsEqualTo("True:3:1");
    }
}
