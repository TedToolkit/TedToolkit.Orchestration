using BenchmarkDotNet.Attributes;
using TedToolkit.Orchestration.Pipeline.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

public sealed class ValueSink { public int Value; }

internal static class ValueSteps
{
    [Step]
    internal static int SyncAdd(int value, int amount, CancellationToken token) => value + amount;

    [Step]
    internal static void StoreValue(int value, ValueSink sink, CancellationToken token) =>
        sink.Value = value;
}

public class ValueSyncBenchmarks
{
    private readonly ValueSink _sink = new();
    private SyncValuePipeline.ConfigurationPipeline _runner = null!;

    [GlobalSetup]
    public void Setup()
    {
        _runner = new SyncValuePipeline.ConfigurationPipeline();
        Handwritten();
        if (_sink.Value != 1028) throw new InvalidOperationException();
        _sink.Value = 0;
        Generated();
        if (_sink.Value != 1028) throw new InvalidOperationException();
    }

    [Benchmark(Baseline = true)]
    public void Handwritten() => Execute(1024, _sink, CancellationToken.None);

    private static void Execute(int input, ValueSink sink, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var a = ValueSteps.SyncAdd(input, 1, token);
        token.ThrowIfCancellationRequested();
        var b = ValueSteps.SyncAdd(a, 1, token);
        token.ThrowIfCancellationRequested();
        var c = ValueSteps.SyncAdd(b, 1, token);
        token.ThrowIfCancellationRequested();
        var d = ValueSteps.SyncAdd(c, 1, token);
        token.ThrowIfCancellationRequested();
        ValueSteps.StoreValue(d, sink, token);
        token.ThrowIfCancellationRequested();
        
    }

    [Benchmark] public void Generated() => _ = _runner.Execute(1024, _sink);
}

public class ValueAsyncBenchmarks
{
    private readonly ValueSink _sink = new();
    private AsyncValuePipeline.ConfigurationPipeline _runner = null!;
    [Params(WorkMode.Completed, WorkMode.Yield)] public WorkMode Mode { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _runner = new AsyncValuePipeline.ConfigurationPipeline();
        await Handwritten();
        if (_sink.Value != 1028) throw new InvalidOperationException();
        _sink.Value = 0;
        await Generated();
        if (_sink.Value != 1028) throw new InvalidOperationException();
    }

    [Benchmark(Baseline = true)]
    public async Task Handwritten()
    {
        var token = CancellationToken.None;
        token.ThrowIfCancellationRequested();
        var a = await Work.Add(1024, 1, Mode).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var b = await Work.Add(a, 1, Mode).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var c = await Work.Add(b, 1, Mode).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var d = await Work.Add(c, 1, Mode).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        _sink.Value = d;
        token.ThrowIfCancellationRequested();
    }

    [Benchmark] public Task Generated() => _runner.ExecuteAsync(1024, _sink, Mode);
}

internal static partial class SyncValuePipeline
{
    [Pipeline]
    public static void Configuration(StepGraph p, int input, ValueSink sink)
    {
        var a = p.SyncAdd(input, 1);
        var b = p.SyncAdd(a, 1);
        var c = p.SyncAdd(b, 1);
        var d = p.SyncAdd(c, 1);
        p.StoreValue(d, sink);
    }
}
internal static partial class AsyncValuePipeline
{
    [Pipeline]
    public static void Configuration(StepGraph p, int input, ValueSink sink, WorkMode mode)
    {
        var a = p.AddStep(input, 1, mode);
        var b = p.AddStep(a, 1, mode);
        var c = p.AddStep(b, 1, mode);
        var d = p.AddStep(c, 1, mode);
        p.StoreValue(d, sink);
    }
}



