namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Starts an asynchronous operation whose state outlives the stack-only step.</summary>
internal static class DelayStepMethods
{
    /// <summary>Returns the value after a cooperative delay.</summary>
    [Attributes.Step]
    internal static Task<int> DelayStep(int value, CancellationToken cancellationToken) =>
        DelayAsync(value, cancellationToken);

    private static async Task<int> DelayAsync(int value, CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        return value;
    }
}

