using BenchmarkDotNet.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

public sealed class ValueSink { public int Value; }

internal readonly ref struct SyncAdd(int value, int amount) : IStep<int>
{
    public int Execute(CancellationToken token) => value + amount;
}

internal readonly ref struct StoreValue(int value, ValueSink sink) : IStep
{
    public void Execute(CancellationToken token) => sink.Value = value;
}

public class ValueSyncBenchmarks
{
    private readonly ValueSink _sink = new();
    private SyncValuePipeline _runner = null!;

    [GlobalSetup]
    public void Setup()
    {
        _runner = new SyncValuePipeline(EmptyServices.Instance, _sink);
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
        var a = new SyncAdd(input, 1).Execute(token);
        token.ThrowIfCancellationRequested();
        var b = new SyncAdd(a, 1).Execute(token);
        token.ThrowIfCancellationRequested();
        var c = new SyncAdd(b, 1).Execute(token);
        token.ThrowIfCancellationRequested();
        var d = new SyncAdd(c, 1).Execute(token);
        token.ThrowIfCancellationRequested();
        new StoreValue(d, sink).Execute(token);
        token.ThrowIfCancellationRequested();
        
    }

    [Benchmark] public void Generated() => _runner.ExecuteWithoutResults(1024);
}

public class ValueAsyncBenchmarks
{
    private readonly ValueSink _sink = new();
    private AsyncValuePipeline _runner = null!;
    [Params(WorkMode.Completed, WorkMode.Yield)] public WorkMode Mode { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _runner = new AsyncValuePipeline(EmptyServices.Instance, _sink, Mode);
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

    [Benchmark] public Task Generated() => _runner.ExecuteWithoutResultsAsync(1024);
}

internal partial class SyncValuePipeline : global::TedToolkit.Orchestration.Pipeline.Pipeline
{
    private void Configure(Builder p, ValueSink sink)
    {
        var a = p.SyncAdd(amount: 1);
        var b = p.SyncAdd(a, 1);
        var c = p.SyncAdd(b, 1);
        var d = p.SyncAdd(c, 1);
        p.StoreValue(d, sink);
    }
}
internal partial class AsyncValuePipeline : global::TedToolkit.Orchestration.Pipeline.Pipeline
{
    private void Configure(Builder p, ValueSink sink, WorkMode mode)
    {
        var a = p.AddStep(amount: 1, mode: mode);
        var b = p.AddStep(a, 1, mode);
        var c = p.AddStep(b, 1, mode);
        var d = p.AddStep(c, 1, mode);
        p.StoreValue(d, sink);
    }
}



