namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task UncancelablePolicyFreeStepsPreserveTheBusinessResult(bool completed)
    {
        var result = await Run("""
            internal readonly ref partial struct Work(Task<int> operation) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => operation;
            }
            [CompositeStep]
            public readonly ref partial struct Example(Task<int> operation)
            {
                private void Configuration(StepGraph p) { var work = p.Work(operation); }
            }
            """ + AsyncScenario("""
                var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<int> task = COMPLETED ? Task.FromResult(42) : pending.Task;
                var execution = new Example.Pipeline().ExecuteAsync(task);
                pending.TrySetResult(42);
                return $"{await task}:{(await execution).Work}";
                """.Replace("COMPLETED", completed ? "true" : "false")));
        await Assert.That(result).IsEqualTo("42:42");
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task PolicyFreeAsyncStepsStillWaitAndPreserveCallerCancellation(bool resultless)
    {
        var step = resultless
            ? "internal readonly ref partial struct Work(Task operation) : IAsyncStep { public Task ExecuteAsync(CancellationToken token) => operation; }"
            : "internal readonly ref partial struct Work(Task<int> operation) : IAsyncStep<int> { public Task<int> ExecuteAsync(CancellationToken token) => operation; }";
        var result = await Run(step + """
            [CompositeStep]
            public readonly ref partial struct Example(Task<int> operation)
            {
                private void Configuration(StepGraph p) { var work = p.Work(operation); }
            }
            """ + AsyncScenario("""
                using var cancellation = new CancellationTokenSource();
                var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var execution = new Example.Pipeline().ExecuteAsync(release.Task, cancellation.Token);
                cancellation.Cancel();
                var early = execution.IsCompleted;
                release.SetResult(42);
                try { await execution; return "unexpected"; }
                catch (OperationCanceledException failure) { return $"{early}:{failure.CancellationToken == cancellation.Token}"; }
                """));
        await Assert.That(result).IsEqualTo("False:True");
    }
}
