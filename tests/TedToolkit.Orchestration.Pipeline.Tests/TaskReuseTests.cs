namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task UncancelablePolicyFreeStepsPreserveTheBusinessResult(bool completed)
    {
        var result = await Run("""
            internal static class WorkStepMethods
            {
                [Step]
                internal static Task<int> Work(Task<int> operation, CancellationToken token) => operation;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, Task<int> operation) { var work = p.Work(operation); }
            }
            """ + AsyncScenario("""
                var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<int> task = COMPLETED ? Task.FromResult(42) : pending.Task;
                var execution = new Example.ConfigurationPipeline().ExecuteAsync(task);
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
            ? "internal static class WorkSteps { [Step] internal static Task Work(Task operation, CancellationToken token) => operation; }"
            : "internal static class WorkSteps { [Step] internal static Task<int> Work(Task<int> operation, CancellationToken token) => operation; }";
        var result = await Run(step + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, Task<int> operation) { var work = p.Work(operation); }
            }
            """ + AsyncScenario("""
                using var cancellation = new CancellationTokenSource();
                var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var execution = new Example.ConfigurationPipeline().ExecuteAsync(release.Task, cancellation.Token);
                cancellation.Cancel();
                var early = execution.IsCompleted;
                release.SetResult(42);
                try { await execution; return "unexpected"; }
                catch (OperationCanceledException failure) { return $"{early}:{failure.CancellationToken == cancellation.Token}"; }
                """));
        await Assert.That(result).IsEqualTo("False:True");
    }
}
