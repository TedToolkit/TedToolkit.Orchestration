namespace TedToolkit.Orchestration.Pipeline;

/// <summary>Provides compile-time configuration and common attempt policy helpers.</summary>
public abstract class Pipeline
{
    /// <summary>Declares the compile-time graph. Generated execution never calls this method.</summary>
    /// <param name="pipeline">The shared configuration builder.</param>
    protected virtual void Configuration(Builder pipeline) { }

    /// <summary>An empty compile-time receiver for generated step declarations.</summary>
    public readonly struct Builder;

    /// <summary>Signals cancellation without replacing the original Step failure with callback failures.</summary>
    /// <param name="cancellation">The current invocation's shared cancellation source.</param>
    /// <returns>Completion of the cancellation callbacks.</returns>
    protected static async Task CancelRemainingAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failing cancellation callback must not mask the Step failure.
        }
    }

    /// <summary>Preserves cancellation semantics while reusing the business Task whenever possible.</summary>
    /// <typeparam name="T">The Step result type.</typeparam>
    /// <param name="operation">The business operation.</param>
    /// <param name="token">The execution cancellation token.</param>
    /// <returns>The original task when no additional observation is required.</returns>
    protected static Task<T> ObserveStepAsync<T>(Task<T> operation, CancellationToken token)
    {
        if (!token.CanBeCanceled) return operation;
        if (operation.IsCompletedSuccessfully)
        {
            token.ThrowIfCancellationRequested();
            return operation;
        }
        return ObserveCancelableAsync(operation, token);
    }

    /// <summary>Preserves cancellation semantics while reusing a resultless business Task whenever possible.</summary>
    /// <param name="operation">The business operation.</param>
    /// <param name="token">The execution cancellation token.</param>
    /// <returns>The original task when no additional observation is required.</returns>
    protected static Task ObserveStepAsync(Task operation, CancellationToken token)
    {
        if (!token.CanBeCanceled) return operation;
        if (operation.IsCompletedSuccessfully)
        {
            token.ThrowIfCancellationRequested();
            return operation;
        }
        return ObserveCancelableAsync(operation, token);
    }

    private static async Task<T> ObserveCancelableAsync<T>(Task<T> operation, CancellationToken token)
    {
        try
        {
            var result = await operation.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch
        {
            token.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static async Task ObserveCancelableAsync(Task operation, CancellationToken token)
    {
        try
        {
            await operation.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch
        {
            token.ThrowIfCancellationRequested();
            throw;
        }
    }
    /// <summary>Owns retry state and the timeout resource for successive step attempts.</summary>
    protected struct StepAttempt : IDisposable
    {
        private readonly int _timeoutMilliseconds;
        private readonly CancellationToken _executionToken;
        private int _remainingRetries;
        private CancellationTokenSource? _timeout;
        private bool _started;
        private Exception? _failure;

        /// <summary>Records a retryable failure or immediately propagates a terminal failure.</summary>
        /// <param name="failure">The failed attempt exception.</param>
        public void RetryOrThrow(Exception failure)
        {
            _executionToken.ThrowIfCancellationRequested();
            if (_timeout?.IsCancellationRequested == true)
            {
                if (_remainingRetries == 0)
                    throw new TimeoutException("The step attempt exceeded its timeout.", failure);
            }
            else if (failure is OperationCanceledException || _remainingRetries == 0)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            _failure = failure;
        }

        /// <summary>Gets the token for the current attempt.</summary>
        public readonly CancellationToken Token => _timeout?.Token ?? _executionToken;

        /// <summary>Creates the policy state for one step invocation.</summary>
        /// <param name="retryCount">The number of additional attempts.</param>
        /// <param name="timeoutMilliseconds">The per-attempt timeout, or -1 for infinite.</param>
        /// <param name="executionToken">The invocation cancellation token.</param>
        public StepAttempt(int retryCount, int timeoutMilliseconds, CancellationToken executionToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(retryCount);
            if (timeoutMilliseconds != -1 && timeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            _remainingRetries = retryCount;
            _timeoutMilliseconds = timeoutMilliseconds;
            _executionToken = executionToken;
        }

        /// <summary>Begins the first attempt or advances after a recorded failure.</summary>
        /// <returns>True for a fresh attempt, or false after success. Terminal failures are rethrown.</returns>
        public bool Begin()
        {
            _executionToken.ThrowIfCancellationRequested();
            if (_started)
            {
                if (_failure is null) return false;
                _remainingRetries--;
            }
            Dispose();
            _failure = null;
            _started = true;
            if (_timeoutMilliseconds != -1)
            {
                _timeout = CancellationTokenSource.CreateLinkedTokenSource(_executionToken);
                _timeout.CancelAfter(_timeoutMilliseconds);
            }
            return true;
        }
        /// <summary>Releases the current timeout source.</summary>
        public void Dispose()
        {
            _timeout?.Dispose();
            _timeout = null;
        }
    }
}
