using BenchmarkDotNet.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

// Both candidates return Task<int> and enter an async method, so the comparison
// does not confuse result access with changing the caller's return contract.
public class CompletedTaskReadBenchmarks
{
    private const int Count = 256;
    private Task<int>[] _tasks = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tasks = Enumerable.Range(1024, Count).Select(Task.FromResult).ToArray();
        var expected = Enumerable.Range(1024, Count).Sum();
        if (_tasks.Any(task => !task.IsCompletedSuccessfully) ||
            GetResult().GetAwaiter().GetResult() != expected ||
            AwaitCompleted().GetAwaiter().GetResult() != expected)
            throw new InvalidOperationException("Completed task read verification failed.");
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Count)]
    public async Task<int> GetResult()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var sum = 0;
        foreach (var task in _tasks) sum += task.GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public async Task<int> AwaitCompleted()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var sum = 0;
        foreach (var task in _tasks) sum += await task.ConfigureAwait(false);
        return sum;
    }
}

// Matches generated Step methods that first await all typed upstream inputs.
public class CompletedTaskJoinBenchmarks
{
    private Task<int> _left = null!;
    private Task<int> _right = null!;

    [GlobalSetup]
    public void Setup()
    {
        _left = Task.FromResult(1024);
        _right = Task.FromResult(2048);
        if (GetResult().GetAwaiter().GetResult() != 3072 ||
            AwaitCompleted().GetAwaiter().GetResult() != 3072)
            throw new InvalidOperationException("Completed task join verification failed.");
    }

    [Benchmark(Baseline = true)]
    public async Task<int> GetResult()
    {
        await Task.WhenAll(_left, _right).ConfigureAwait(false);
        return _left.GetAwaiter().GetResult() + _right.GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task<int> AwaitCompleted()
    {
        await Task.WhenAll(_left, _right).ConfigureAwait(false);
        return await _left.ConfigureAwait(false) + await _right.ConfigureAwait(false);
    }
}

// Matches result extraction after the pipeline has drained all Step tasks.
// The explicit Task cast selects the non-generic WhenAll used by CompleteAsync.
public class CompletedTaskSnapshotBenchmarks
{
    private Task<int> _left = null!;
    private Task<int> _right = null!;

    [GlobalSetup]
    public void Setup()
    {
        _left = Task.FromResult(1024);
        _right = Task.FromResult(2048);
        if (GetResult().GetAwaiter().GetResult() != 3072 ||
            AwaitCompleted().GetAwaiter().GetResult() != 3072)
            throw new InvalidOperationException("Completed task snapshot verification failed.");
    }

    [Benchmark(Baseline = true)]
    public async Task<int> GetResult()
    {
        await Task.WhenAll((Task)_left, _right).ConfigureAwait(false);
        return _left.GetAwaiter().GetResult() + _right.GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task<int> AwaitCompleted()
    {
        await Task.WhenAll((Task)_left, _right).ConfigureAwait(false);
        return await _left.ConfigureAwait(false) + await _right.ConfigureAwait(false);
    }
}
