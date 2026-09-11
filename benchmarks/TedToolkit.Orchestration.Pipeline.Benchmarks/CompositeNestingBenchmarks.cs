using BenchmarkDotNet.Attributes;
using TedToolkit.Orchestration.Pipeline.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

[MemoryDiagnoser]
public class CompositeNestingBenchmarks
{
    private readonly FlatPair.ConfigurationPipeline _flat = new();
    private readonly NestedPair.ConfigurationPipeline _nested = new();

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
    private readonly FlatSyncPair.ConfigurationPipeline _flat = new();
    private readonly NestedSyncPair.ConfigurationPipeline _nested = new();

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

internal static partial class FlatPair
{
    [Pipeline]
    public static void Configuration(StepGraph steps, int input, WorkMode mode)
    {
        var first = steps.AddStep(input, 1, mode);
        var output = steps.AddStep(first, 1, mode);
    }
}

internal static partial class Pair
{
    [Pipeline]
    public static void Configuration(StepGraph steps, int input, WorkMode mode)
    {
        var first = steps.AddStep(input, 1, mode);
        var output = steps.AddStep(first, 1, mode);
    }
}

internal static partial class NestedPair
{
    [Pipeline]
    public static void Configuration(StepGraph steps, int input, WorkMode mode)
    {
        var pair = steps.Pair(input, mode);
    }
}

internal static partial class FlatSyncPair
{
    [Pipeline]
    public static void Configuration(StepGraph steps, int input)
    {
        var first = steps.SyncAdd(input, 1);
        var output = steps.SyncAdd(first, 1);
    }
}

internal static partial class SyncPair
{
    [Pipeline]
    public static void Configuration(StepGraph steps, int input)
    {
        var first = steps.SyncAdd(input, 1);
        var output = steps.SyncAdd(first, 1);
    }
}

internal static partial class NestedSyncPair
{
    [Pipeline]
    public static void Configuration(StepGraph steps, int input)
    {
        var pair = steps.SyncPair(input);
    }
}
