namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    private const string ConcurrentSteps = """
        internal static class StartStepMethods
        {
            [Step]
            internal static Task<int> Start(Func<CancellationToken, Task<int>> work, CancellationToken token) => work(token);
        }
        internal static class AfterStepMethods
        {
            [Step]
            internal static Task<int> After(int value, Func<int, CancellationToken, Task<int>> work, CancellationToken token) => work(value, token);
        }
        """;

    [Test]
    public async Task ChildStartsAsSoonAsItsOwnDependencyCompletesAndRootRunsOnce()
    {
        var result = await Run(NamedSteps + ConcurrentSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, Func<CancellationToken, Task<int>> rootWork,
                Func<int, CancellationToken, Task<int>> slowWork,
                Func<int, CancellationToken, Task<int>> fastWork,
                Func<int, CancellationToken, Task<int>> childWork)
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
                var execution = new Example.ConfigurationPipeline().ExecuteAsync(
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
            Task execution = new Example.ConfigurationPipeline().METHOD(
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
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, Func<CancellationToken, Task<int>> rootWork,
                Func<int, CancellationToken, Task<int>> slowWork,
                Func<int, CancellationToken, Task<int>> failWork)
                {
                    var root = p.Start(rootWork); var slow = p.After(root, slowWork);
                    var fail = p.After(root, failWork); p.Add(slow, fail);
                }
            }
            """ + AsyncScenario(body));
        await Assert.That(result).IsEqualTo("True:1");
    }

    [Test]
    public async Task CancellationCallbackFailureDoesNotReplaceStepFailure()
    {
        var result = await Run(NamedSteps + ConcurrentSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, Func<CancellationToken, Task<int>> slowWork,
                Func<CancellationToken, Task<int>> failWork)
                {
                    var slow = p.Start(slowWork); var fail = p.Start(failWork); p.Add(slow, fail);
                }
            }
            """ + AsyncScenario("""
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var expected = new InvalidOperationException("primary");
                var callbackFailure = new InvalidOperationException("callback");
                var execution = new Example.ConfigurationPipeline().ExecuteAsync(
                    slowWork: async token =>
                    {
                        using var registration = token.Register(() => throw callbackFailure);
                        started.SetResult();
                        await Task.Delay(-1, token);
                        return 1;
                    },
                    failWork: async token => { await started.Task; throw expected; });
                try { await execution; return "unexpected"; }
                catch (Exception actual) { return $"{ReferenceEquals(actual, expected)}:{ReferenceEquals(actual, callbackFailure)}"; }
                """));

        await Assert.That(result).IsEqualTo("True:False");
    }

    [Test]
    public async Task ConcurrentInvocationsKeepInputsAndResultsSeparate()
    {
        var result = await Run(ConcurrentSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, Func<CancellationToken, Task<int>> work) { var node = p.Start(work); }
            }
            """ + AsyncScenario("""
                var e = new Example.ConfigurationPipeline();
                var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var a = e.ExecuteAsync(_ => first.Task);
                var b = await e.ExecuteAsync(_ => Task.FromResult(2));
                first.SetResult(1);
                return $"{(await a).Node}:{b.Node}";
                """));
        await Assert.That(result).IsEqualTo("1:2");
    }
}


