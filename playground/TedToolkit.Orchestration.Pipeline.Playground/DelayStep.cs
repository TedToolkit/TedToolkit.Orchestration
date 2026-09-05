using TedToolkit.Orchestration.Pipeline.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Starts an asynchronous operation whose state outlives the stack-only step.</summary>
[StepPolicy(RetryCount = 1, TimeoutMilliseconds = 2000)]
internal readonly ref struct DelayStep(int value) : IAsyncStep<int>
{
    /// <inheritdoc />
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default) => DelayAsync(value, cancellationToken);

    private static async Task<int> DelayAsync(int value, CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        return value;
    }
}

