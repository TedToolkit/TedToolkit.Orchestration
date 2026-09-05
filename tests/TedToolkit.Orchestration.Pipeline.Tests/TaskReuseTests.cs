namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task UncancelablePolicyFreeStepsReuseTheBusinessTask(bool completed)
    {
        var result = await Run("""
            internal readonly ref struct Work(Task<int> operation) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => operation;
            }
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { var work = p.Work(); }
                public Task<int> Probe(Task<int> task) => RunWork0Async(task, CancellationToken.None);
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<int> task = COMPLETED ? Task.FromResult(42) : pending.Task;
                var same = ReferenceEquals(task, new Example(services).Probe(task));
                pending.TrySetResult(42);
                return $"{same}:{await task}";
                """.Replace("COMPLETED", completed ? "true" : "false")));
        await Assert.That(result).IsEqualTo("True:42");
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task PolicyFreeAsyncStepsStillWaitAndPreserveCallerCancellation(bool resultless)
    {
        var step = resultless
            ? "internal readonly ref struct Work(Task operation) : IAsyncStep { public Task ExecuteAsync(CancellationToken token) => operation; }"
            : "internal readonly ref struct Work(Task<int> operation) : IAsyncStep<int> { public Task<int> ExecuteAsync(CancellationToken token) => operation; }";
        var result = await Run(step + """
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { var work = p.Work(); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                using var cancellation = new CancellationTokenSource();
                var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var execution = new Example(services).ExecuteAsync(release.Task, cancellation.Token);
                cancellation.Cancel();
                var early = execution.IsCompleted;
                release.SetResult(42);
                try { await execution; return "unexpected"; }
                catch (OperationCanceledException failure) { return $"{early}:{failure.CancellationToken == cancellation.Token}"; }
                """));
        await Assert.That(result).IsEqualTo("False:True");
    }
}
