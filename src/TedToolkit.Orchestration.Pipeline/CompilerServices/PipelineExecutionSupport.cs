namespace TedToolkit.Orchestration.Pipeline.CompilerServices;

/// <summary>Runtime support for generated Pipeline execution code.</summary>
[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
public static class PipelineExecutionSupport
{
    /// <summary>Cancels and observes the remaining work without replacing the primary failure.</summary>
    /// <param name="cancellation">The invocation cancellation source.</param>
    public static async Task CancelRemainingAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Observes a result-bearing operation against the invocation cancellation boundary.</summary>
    /// <typeparam name="T">The operation result type.</typeparam>
    /// <param name="operation">The operation to observe.</param>
    /// <param name="token">The invocation cancellation token.</param>
    /// <returns>The original operation when no wrapper is needed; otherwise an observing task.</returns>
    public static Task<T> ObserveStepAsync<T>(Task<T> operation, CancellationToken token)
    {
        if (!token.CanBeCanceled) return operation;
        if (operation.IsCompletedSuccessfully)
        {
            token.ThrowIfCancellationRequested();
            return operation;
        }
        return ObserveCancelableAsync(operation, token);
    }

    /// <summary>Observes a completion-only operation against the invocation cancellation boundary.</summary>
    /// <param name="operation">The operation to observe.</param>
    /// <param name="token">The invocation cancellation token.</param>
    /// <returns>The original operation when no wrapper is needed; otherwise an observing task.</returns>
    public static Task ObserveStepAsync(Task operation, CancellationToken token)
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

    /// <summary>Owns retry state and timeout resources for successive Step attempts.</summary>
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public struct StepAttempt : IDisposable
    {
        private readonly int _timeoutMilliseconds;
        private readonly CancellationToken _executionToken;
        private int _remainingRetries;
        private CancellationTokenSource? _timeout;
        private bool _started;
        private Exception? _failure;

        /// <summary>Gets the cancellation token for the current attempt.</summary>
        public readonly CancellationToken Token => _timeout?.Token ?? _executionToken;

        /// <summary>Creates retry and timeout state for one generated Step invocation.</summary>
        /// <param name="retryCount">The number of retries after the first attempt.</param>
        /// <param name="timeoutMilliseconds">The per-attempt timeout, or -1 for no timeout.</param>
        /// <param name="executionToken">The invocation cancellation token.</param>
        public StepAttempt(int retryCount, int timeoutMilliseconds, CancellationToken executionToken)
        {
            if (retryCount < 0) throw new ArgumentOutOfRangeException(nameof(retryCount));
            if (timeoutMilliseconds != -1 && timeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            _remainingRetries = retryCount;
            _timeoutMilliseconds = timeoutMilliseconds;
            _executionToken = executionToken;
            _timeout = null;
            _started = false;
            _failure = null;
        }

        /// <summary>Starts the next attempt when one remains.</summary>
        /// <returns><see langword="true"/> when the generated caller should execute an attempt.</returns>
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

        /// <summary>Records a retryable failure or rethrows the terminal failure.</summary>
        /// <param name="failure">The failure produced by the current attempt.</param>
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
                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            _failure = failure;
        }

        /// <summary>Releases timeout resources owned by the current attempt.</summary>
        public void Dispose()
        {
            _timeout?.Dispose();
            _timeout = null;
        }
    }
}
