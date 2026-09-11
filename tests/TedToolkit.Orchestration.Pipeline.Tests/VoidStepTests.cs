namespace TedToolkit.Orchestration.Pipeline.Tests;

public class VoidStepTests
{
    [Test]
    public async Task GeneratedVoidExecutionsUseTheRetryAttribute()
    {
        var attempts = 0;
        Effect step = new Effect(_ => ++attempts % 2 == 1
            ? Task.FromException(new InvalidOperationException()) : Task.CompletedTask, retries: 1);
        await step;
        await step.ExecuteWithPolicyAsync();
        await Assert.That(attempts).IsEqualTo(4);
    }

    [Test]
    public async Task VoidTimeoutWaitsForAnAttemptBeforeRetrying()
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var step = new Effect(async token =>
        {
            if (++attempts != 1) return;
            using var registration = token.Register(() => canceled.TrySetResult());
            await release.Task;
        }, retries: 1, timeout: TimeSpan.FromMilliseconds(50));
        var execution = step.ExecuteWithPolicyAsync();
        try
        {
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(execution.IsCompleted).IsFalse();
            await Assert.That(attempts).IsEqualTo(1);
        }
        finally { release.TrySetResult(); }
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(attempts).IsEqualTo(2);
    }

    internal sealed class Effect
    {
        private readonly Func<CancellationToken, Task> _execute;
        private readonly Task<Func<Func<CancellationToken, Task>, CancellationToken, Task>> _run;
        internal Effect(Func<CancellationToken, Task> execute, int retries = 0, TimeSpan? timeout = null)
        {
            _execute = execute;
            var milliseconds = timeout is null ? -1 : checked((int)timeout.Value.TotalMilliseconds);
            _run = ExecutorGeneratorTests.Compile<Func<Func<CancellationToken, Task>, CancellationToken, Task>>($$"""
                internal static class EffectStepMethods
                {
                    [Step]
                    internal static Task Effect(Func<CancellationToken, Task> operation, CancellationToken token) => operation(token);
                }
                public static partial class EffectPipeline
                {
                    [Pipeline]
                    public static void Configuration(StepGraph pipeline, Func<CancellationToken, Task> operation) { pipeline.Effect(operation).WithRetry({{retries}}).WithTimeout({{milliseconds}}); }
                }
                public static class Scenario
                {
                    public static Task Run(Func<CancellationToken, Task> operation, CancellationToken token)
                    {
                        return new EffectPipeline.ConfigurationPipeline().ExecuteAsync(operation, token);
                    }
                }
                """);
        }
        public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => ExecuteWithPolicyAsync().GetAwaiter();
        public async Task ExecuteWithPolicyAsync(CancellationToken token = default) => await (await _run)(_execute, token);
    }
}


