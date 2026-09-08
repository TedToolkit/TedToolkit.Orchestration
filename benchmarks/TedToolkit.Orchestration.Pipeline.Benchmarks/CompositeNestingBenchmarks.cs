using BenchmarkDotNet.Attributes;
using TedToolkit.Orchestration.Pipeline.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

[MemoryDiagnoser]
public class CompositeNestingBenchmarks
{
    private readonly FlatPair.Pipeline _flat = new();
    private readonly NestedPair.Pipeline _nested = new();

    [Params(WorkMode.Completed, WorkMode.Yield)]
    public WorkMode Mode { get; set; }

    [GlobalSetup]
    public async Task Verify()
    {
        const int expected = 1026;
        if (await Direct() != expected ||
            await Flat() != expected ||
            await Nested() != expected)
            throw new InvalidOperationException("Composite benchmark produced an unexpected result.");
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Direct()
    {
        var first = await Work.Add(1024, 1, Mode).ConfigureAwait(false);
        return await Work.Add(first, 1, Mode).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<int> Flat() =>
        (await _flat.ExecuteAsync(1024, Mode).ConfigureAwait(false)).Output;

    [Benchmark]
    public async Task<int> Nested() =>
        (await _nested.ExecuteAsync(1024, Mode).ConfigureAwait(false)).Pair.Output;
}

[MemoryDiagnoser]
public class CompositeSyncNestingBenchmarks
{
    private readonly FlatSyncPair.Pipeline _flat = new();
    private readonly NestedSyncPair.Pipeline _nested = new();

    [Params(1024)]
    public int Input { get; set; }

    [GlobalSetup]
    public void Verify()
    {
        var expected = Input + 2;
        if (Flat() != expected || Nested() != expected)
            throw new InvalidOperationException("Synchronous Composite benchmark produced an unexpected result.");
    }

    [Benchmark(Baseline = true)]
    public int Flat() => _flat.Execute(Input).Output;

    [Benchmark]
    public int Nested() => _nested.Execute(Input).Pair.Output;
}

[CompositeStep]
internal readonly ref partial struct FlatPair(int input, WorkMode mode)
{
    private void Configuration(StepGraph steps)
    {
        var first = steps.AddStep(input, 1, mode);
        var output = steps.AddStep(first, 1, mode);
    }
}

[CompositeStep]
internal readonly ref partial struct Pair(int input, WorkMode mode)
{
    private void Configuration(StepGraph steps)
    {
        var first = steps.AddStep(input, 1, mode);
        var output = steps.AddStep(first, 1, mode);
    }
}

[CompositeStep]
internal readonly ref partial struct NestedPair(int input, WorkMode mode)
{
    private void Configuration(StepGraph steps)
    {
        var pair = steps.Pair(input, mode);
    }
}

[CompositeStep]
internal readonly ref partial struct FlatSyncPair(int input)
{
    private void Configuration(StepGraph steps)
    {
        var first = steps.SyncAdd(input, 1);
        var output = steps.SyncAdd(first, 1);
    }
}

[CompositeStep]
internal readonly ref partial struct SyncPair(int input)
{
    private void Configuration(StepGraph steps)
    {
        var first = steps.SyncAdd(input, 1);
        var output = steps.SyncAdd(first, 1);
    }
}

[CompositeStep]
internal readonly ref partial struct NestedSyncPair(int input)
{
    private void Configuration(StepGraph steps)
    {
        var pair = steps.SyncPair(input);
    }
}
