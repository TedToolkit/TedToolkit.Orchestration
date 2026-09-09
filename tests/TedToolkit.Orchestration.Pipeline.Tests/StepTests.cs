namespace TedToolkit.Orchestration.Pipeline.Tests;

public class StepTests
{
    [Test]
    public async Task AwaitReturnsTheAsynchronousResult()
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var step = new TestStep(_ => result.Task);
        await step.Ready;
        var execution = AwaitStep(step);

        await Assert.That(execution.IsCompleted).IsFalse();
        result.SetResult(42);
        await Assert.That(await execution).IsEqualTo(42);
    }

    [Test]
    public async Task EachAwaitExecutesAgain()
    {
        var attempts = 0;
        TestStep step = new TestStep(_ => Task.FromResult(++attempts));

        await Assert.That(await step).IsEqualTo(1);
        await Assert.That(await step).IsEqualTo(2);
    }

    [Test]
    public async Task DefaultTimeoutDoesNotCancelTheAttempt()
    {
        CancellationToken token = default;
        var step = new DefaultStep(cancellationToken =>
        {
            token = cancellationToken;
            return Task.FromResult(42);
        });

        await Assert.That(await step).IsEqualTo(42);
        await Assert.That(token.CanBeCanceled).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DefaultRetryCountPropagatesTheFirstFailure(bool asynchronousFailure)
    {
        var attempts = 0;
        var expected = new InvalidOperationException("original failure");
        var step = new DefaultStep(_ =>
        {
            attempts++;
            return asynchronousFailure ? Task.FromException<int>(expected) : throw expected;
        });

        var exception = await Assert.That(async () => await step).Throws<InvalidOperationException>();
        await Assert.That(exception).IsSameReferenceAs(expected);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetryCountAllowsAdditionalAttemptsUntilSuccess(bool asynchronousFailure)
    {
        var attempts = 0;
        var step = new TestStep(_ =>
        {
            if (++attempts == 3)
            {
                return Task.FromResult(42);
            }

            var failure = new InvalidOperationException();
            return asynchronousFailure ? Task.FromException<int>(failure) : throw failure;
        }, retries: 4);

        await Assert.That(await step).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task ExhaustedRetriesPropagateTheLastFailure()
    {
        var attempts = 0;
        Exception? lastFailure = null;
        var step = new TestStep(_ =>
        {
            lastFailure = new InvalidOperationException($"attempt {++attempts}");
            return Task.FromException<int>(lastFailure);
        }, retries: 2);

        var exception = await Assert.That(async () => await step).Throws<InvalidOperationException>();
        await Assert.That(exception).IsSameReferenceAs(lastFailure);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationIsNotRetried(bool finiteTimeout)
    {
        var attempts = 0;
        var step = new TestStep(_ =>
        {
            attempts++;
            return Task.FromCanceled<int>(new CancellationToken(true));
        }, retries: 2, timeout: finiteTimeout ? TimeSpan.FromSeconds(30) : null);

        await Assert.That(async () => await step).Throws<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task TimeoutWaitsForTheAttemptBeforeReturning()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var step = new TestStep(token =>
        {
            token.Register(() => canceled.TrySetResult());
            return pending.Task;
        }, timeout: TimeSpan.FromMilliseconds(50));
        await step.Ready;
        var execution = AwaitStep(step);

        try
        {
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(execution.IsCompleted).IsFalse();
        }
        finally
        {
            pending.TrySetResult(42);
        }

        await Assert.That(async () => await execution.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<TimeoutException>();
    }

    [Test]
    public async Task TimeoutDoesNotStartRetryUntilThePreviousAttemptFinishes()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var step = new TestStep(token =>
        {
            if (++attempts > 1)
            {
                return Task.FromResult(42);
            }

            token.Register(() => canceled.TrySetResult());
            return pending.Task;
        }, retries: 1, timeout: TimeSpan.FromMilliseconds(50));
        await step.Ready;
        var execution = AwaitStep(step);

        try
        {
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(attempts).IsEqualTo(1);
            await Assert.That(execution.IsCompleted).IsFalse();
        }
        finally
        {
            pending.TrySetResult(0);
        }

        await Assert.That(await execution.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task AlreadyCanceledInvocationDoesNotExecute()
    {
        var attempts = 0;
        var token = new CancellationToken(true);
        var step = new TestStep(_ => Task.FromResult(++attempts), retries: 2);

        var exception = await Assert.That(async () => await step.ExecuteWithPolicyAsync(token))
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(token);
        await Assert.That(attempts).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CallerCancellationWaitsForCleanupAndNeverRetries(bool finiteTimeout)
    {
        using var cancellation = new CancellationTokenSource();
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var step = new TestStep(token =>
        {
            attempts++;
            token.Register(() => canceled.TrySetResult());
            return cleanup.Task;
        }, retries: 2, timeout: finiteTimeout ? TimeSpan.FromSeconds(30) : null);
        await step.Ready;
        var execution = step.ExecuteWithPolicyAsync(cancellation.Token);
        cancellation.Cancel();

        try
        {
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(execution.IsCompleted).IsFalse();
        }
        finally
        {
            cleanup.TrySetResult(42);
        }

        var exception = await Assert.That(async () => await execution.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task TimedOutAttemptCanRetryWithAFreshTimeout()
    {
        var attempts = 0;
        var tokens = new List<CancellationToken>();
        var step = new TestStep(async token =>
        {
            tokens.Add(token);
            if (++attempts == 1)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }

            token.ThrowIfCancellationRequested();
            return 42;
        }, retries: 1, timeout: TimeSpan.FromMilliseconds(100));

        await Assert.That(await AwaitStep(step).WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(tokens[0].IsCancellationRequested).IsTrue();
        await Assert.That(tokens[1].IsCancellationRequested).IsFalse();
        await Assert.That(tokens[0] == tokens[1]).IsFalse();
    }

    [Test]
    public async Task RepeatedTimeoutsStopAfterTheConfiguredRetries()
    {
        var tokens = new List<CancellationToken>();
        var step = new TestStep(async token =>
        {
            tokens.Add(token);
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 0;
        }, retries: 2, timeout: TimeSpan.FromMilliseconds(50));

        await step.Ready;
        var execution = AwaitStep(step);
        await Assert.That(async () => await execution.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<TimeoutException>();
        await Assert.That(tokens.Count).IsEqualTo(3);
        await Assert.That(tokens.All(token => token.IsCancellationRequested)).IsTrue();
        await Assert.That(tokens.Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task SynchronousWorkCannotReturnSuccessAfterItsTokenTimesOut()
    {
        var timedOut = false;
        var step = new TestStep(token =>
        {
            timedOut = token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            return Task.FromResult(42);
        }, timeout: TimeSpan.FromMilliseconds(50));

        await Assert.That(async () => await step).Throws<TimeoutException>();
        await Assert.That(timedOut).IsTrue();
    }

    [Test]
    public async Task ExplicitExecutionUsesTheSameRetryPolicyAsAwait()
    {
        var attempts = 0;
        var step = new TestStep(_ => ++attempts % 2 == 1
            ? Task.FromException<int>(new InvalidOperationException())
            : Task.FromResult(42), retries: 1);

        await Assert.That(await step.ExecuteWithPolicyAsync()).IsEqualTo(42);
        await Assert.That(await step).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(4);
    }

    private static async Task<int> AwaitStep(TestStep step) => await step;

    // Test harness: every invocation executes a real compiled pipeline, not a second policy implementation.
    internal sealed class DefaultStep(Func<CancellationToken, Task<int>> execute) : TestStep(execute);

    internal class TestStep
    {
        private readonly Func<CancellationToken, Task<int>> _execute;
        private readonly Task<Func<Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>> _run;

        internal TestStep(Func<CancellationToken, Task<int>> execute, int retries = 0, TimeSpan? timeout = null)
        {
            _execute = execute;
            var milliseconds = timeout is null ? -1 : checked((int)timeout.Value.TotalMilliseconds);
            _run = ExecutorGeneratorTests.Compile<Func<Func<CancellationToken, Task<int>>, CancellationToken, Task<int>>>($$"""
                internal readonly ref partial struct PolicyStep(Func<CancellationToken, Task<int>> operation) : IAsyncStep<int>
                {
                    public Task<int> ExecuteAsync(CancellationToken token) => operation(token);
                }
                [CompositeStep]
                public readonly ref partial struct PolicyPipeline(Func<CancellationToken, Task<int>> operation)
                {
                    private void Configuration(StepGraph pipeline) { var node = pipeline.PolicyStep(operation).WithRetry({{retries}}).WithTimeout({{milliseconds}}); }
                }
                public static class Scenario
                {
                    public static async Task<int> Run(Func<CancellationToken, Task<int>> operation, CancellationToken token)
                    {
                        var results = await new PolicyPipeline.Pipeline().ExecuteAsync(operation, token);
                        return results.Node;
                    }
                }
                """);
        }

        internal Task Ready => _run;
        public System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter() => ExecuteWithPolicyAsync().GetAwaiter();
        public async Task<int> ExecuteWithPolicyAsync(CancellationToken token = default) => await (await _run)(_execute, token);
    }
}

