using BenchmarkDotNet.Attributes;
namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

public class ChainBenchmarks
{
    private DirectRunner _direct = null!;
    private TedRunner _ted = null!;
    private WorkflowPipelineRunner _workflow = null!;
    private PipelineNetRunner _pipelineNet = null!;
    private DataflowRunner _dataflow = null!;
    [Params(WorkMode.Completed, WorkMode.Yield)] public WorkMode Mode { get; set; }
    [GlobalSetup]
    public void Setup()
    {
        _direct = new(Mode, false);
        _ted = new(Mode, false);
        _workflow = new(Mode);
        _pipelineNet = new(Mode);
        _dataflow = new(Mode, false);
    }
    [Benchmark(Baseline = true)] public Task<int> DirectTasks() => _direct.RunAsync(1024);
    [Benchmark] public Task<int> TedPipeline() => _ted.RunAsync(1024);
    [Benchmark] public Task<int> WorkflowTypedPipeline() => _workflow.RunAsync(1024);
    [Benchmark] public Task<int> PipelineNet() => _pipelineNet.RunAsync(1024);
    [Benchmark] public Task<int> TplDataflow() => _dataflow.RunAsync(1024);
    [GlobalCleanup] public async Task Cleanup() => await _dataflow.DisposeAsync();
}
public class DiamondBenchmarks
{
    private DirectRunner _direct = null!;
    private TedRunner _ted = null!;
    private WorkflowDiamondRunner _workflow = null!;
    private DataflowRunner _dataflow = null!;
    [Params(WorkMode.Completed, WorkMode.Yield)] public WorkMode Mode { get; set; }
    [GlobalSetup]
    public void Setup()
    {
        _direct = new(Mode, true);
        _ted = new(Mode, true);
        _workflow = new(Mode);
        _dataflow = new(Mode, true);
    }
    [Benchmark(Baseline = true)] public Task<int> DirectTasks() => _direct.RunAsync(1024);
    [Benchmark] public Task<int> TedPipeline() => _ted.RunAsync(1024);
    [Benchmark] public Task<int> WorkflowParallel() => _workflow.RunAsync(1024);
    [Benchmark] public Task<int> TplDataflow() => _dataflow.RunAsync(1024);
    [GlobalCleanup] public async Task Cleanup() => await _dataflow.DisposeAsync();
}
public class ConstructionBenchmarks
{
    // Graph construction only; excludes provider creation, JIT, and source generation.
    [Benchmark] public object TedPipeline() => new TedRunner(WorkMode.Completed, false);
    [Benchmark] public object WorkflowTypedPipeline() => new WorkflowPipelineRunner(WorkMode.Completed);
    [Benchmark] public object PipelineNet() => new PipelineNetRunner(WorkMode.Completed);
}

