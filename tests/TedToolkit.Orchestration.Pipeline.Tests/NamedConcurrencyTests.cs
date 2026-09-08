namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    private const string ConcurrentSteps = """
        internal readonly ref partial struct Start(Func<CancellationToken, Task<int>> work) : IAsyncStep<int>
        {
            public Task<int> ExecuteAsync(CancellationToken token) => work(token);
        }
        internal readonly ref partial struct After(int value, Func<int, CancellationToken, Task<int>> work) : IAsyncStep<int>
        {
            public Task<int> ExecuteAsync(CancellationToken token) => work(value, token);
        }
        """;

    [Test]
    public async Task ChildStartsAsSoonAsItsOwnDependencyCompletesAndRootRunsOnce()
    {
        var result = await Run(NamedSteps + ConcurrentSteps + """
            [CompositeStep]
            public readonly ref partial struct Example(
                Func<CancellationToken, Task<int>> rootWork,
                Func<int, CancellationToken, Task<int>> slowWork,
                Func<int, CancellationToken, Task<int>> fastWork,
                Func<int, CancellationToken, Task<int>> childWork)
            {
                private void Configuration(StepGraph p)
                {
                    var root = p.Start(rootWork); var slow = p.After(root, slowWork); var fast = p.After(root, fastWork);
                    var child = p.After(fast, childWork); var sum = p.Add(slow, child);
                }
            }
            """ + AsyncScenario("""
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var childFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var roots = 0;
                var execution = new Example.Pipeline().ExecuteAsync(
                    rootWork: token => Task.FromResult(++roots),
                    slowWork: async (value, token) => { started.SetResult(); await release.Task; return value + 1; },
                    fastWork: async (value, token) => { await started.Task; return value + 2; },
                    childWork: (value, token) => { childFinished.SetResult(); return Task.FromResult(value + 1); });
                try { await childFinished.Task.WaitAsync(TimeSpan.FromSeconds(3)); if (execution.IsCompleted) return "early"; }
                finally { release.TrySetResult(); }
                return $"{(await execution).Sum}:{roots}";
                """));
        await Assert.That(result).IsEqualTo("6:1");
    }

    [Test]
    [Arguments(false, false)] [Arguments(false, true)]
    [Arguments(true, false)] [Arguments(true, true)]
    public async Task FailureAndCallerCancellationDrainStartedSiblings(bool callerCancels, bool discard)
    {
        var body = """
            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleaned = 0;
            CancellationToken stepToken = default;
            var expected = new InvalidOperationException("original");
            Task execution = new Example.Pipeline().METHOD(
                rootWork: token => Task.FromResult(1),
                slowWork: async (value, token) =>
                {
                    stepToken = token;
                    using var registration = token.Register(() => canceled.TrySetResult());
                    started.SetResult();
                    try { await release.Task; return 1; } finally { cleaned++; }
                },
                failWork: async (value, token) =>
                {
                    await started.Task;
                    FAIL_ACTION
                }, cancellationToken: cancellation.Token);
            try
            {
                await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (execution.IsCompleted || cleaned != 0) throw new Exception("returned before cleanup");
            }
            finally { release.TrySetResult(); }
            try { await execution; return "unexpected"; }
            catch (OperationCanceledException e) { return $"{e.CancellationToken == stepToken && stepToken != cancellation.Token && stepToken.IsCancellationRequested}:{cleaned}"; }
            catch (InvalidOperationException e) { return $"{ReferenceEquals(e, expected)}:{cleaned}"; }
            """.Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")
            .Replace("FAIL_ACTION", callerCancels ? "cancellation.Cancel(); await Task.Delay(-1, token); return 1;" : "throw expected;");
        var result = await Run(NamedSteps + ConcurrentSteps + """
            [CompositeStep]
            public readonly ref partial struct Example(
                Func<CancellationToken, Task<int>> rootWork,
                Func<int, CancellationToken, Task<int>> slowWork,
                Func<int, CancellationToken, Task<int>> failWork)
            {
                private void Configuration(StepGraph p)
                {
                    var root = p.Start(rootWork); var slow = p.After(root, slowWork);
                    var fail = p.After(root, failWork); p.Add(slow, fail);
                }
            }
            """ + AsyncScenario(body));
        await Assert.That(result).IsEqualTo("True:1");
    }

    [Test]
    public async Task ConcurrentInvocationsKeepUnboundParametersAndResultsSeparate()
    {
        var result = await Run(ConcurrentSteps + """
            [CompositeStep]
            public readonly ref partial struct Example(Func<CancellationToken, Task<int>> work)
            {
                private void Configuration(StepGraph p) { var node = p.Start(work); }
            }
            """ + AsyncScenario("""
                var e = new Example.Pipeline();
                var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var a = e.ExecuteAsync(_ => first.Task);
                var b = await e.ExecuteAsync(_ => Task.FromResult(2));
                first.SetResult(1);
                return $"{(await a).Node}:{b.Node}";
                """));
        await Assert.That(result).IsEqualTo("1:2");
    }
}


