namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

public enum WorkMode { Completed, Yield }
internal static class Work
{
    // Identical payload; values stay outside Task<int>'s small-result cache.
    internal static Task<int> Add(int value, int amount, WorkMode mode) => mode == WorkMode.Completed
        ? Task.FromResult(value + amount) : YieldThenAdd(value, amount);
    private static async Task<int> YieldThenAdd(int value, int amount)
    {
        await Task.Yield();
        return value + amount;
    }
}
internal interface IRunner : IAsyncDisposable { Task<int> RunAsync(int input); }
internal sealed class DirectRunner(WorkMode mode, bool diamond) : IRunner
{
    public async Task<int> RunAsync(int input)
    {
        var first = await Work.Add(input, 1, mode).ConfigureAwait(false);
        if (!diamond)
        {
            var second = await Work.Add(first, 1, mode).ConfigureAwait(false);
            var third = await Work.Add(second, 1, mode).ConfigureAwait(false);
            return await Work.Add(third, 1, mode).ConfigureAwait(false);
        }
        var left = Work.Add(first, 1, mode);
        var right = Work.Add(first, 2, mode);
        await Task.WhenAll(left, right).ConfigureAwait(false);
        return await Work.Add(left.Result + right.Result, 0, mode).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

