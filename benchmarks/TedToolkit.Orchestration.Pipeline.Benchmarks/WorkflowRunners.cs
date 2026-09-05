using WorkflowFramework;
namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

internal sealed class WorkflowPipelineRunner : IRunner
{
    private readonly Func<int, CancellationToken, Task<int>> _pipeline;
    internal WorkflowPipelineRunner(WorkMode mode) => _pipeline = WorkflowFramework.Pipeline.Pipeline.Create<int>()
        .Pipe<int>((value, _) => Work.Add(value, 1, mode))
        .Pipe<int>((value, _) => Work.Add(value, 1, mode))
        .Pipe<int>((value, _) => Work.Add(value, 1, mode))
        .Pipe<int>((value, _) => Work.Add(value, 1, mode)).Build();
    public Task<int> RunAsync(int input) => _pipeline(input, CancellationToken.None);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal sealed class WorkflowDiamondRunner : IRunner
{
    private readonly IWorkflow<DiamondState> _workflow;
    internal WorkflowDiamondRunner(WorkMode mode) => _workflow = Workflow.Create<DiamondState>()
        .Step("Source", async context => context.Data.Root = await Work.Add(context.Data.Input, 1, mode).ConfigureAwait(false))
        .Parallel(branches => branches
            .Step(new Branch("Left", async context => context.Data.Left = await Work.Add(context.Data.Root, 1, mode).ConfigureAwait(false)))
            .Step(new Branch("Right", async context => context.Data.Right = await Work.Add(context.Data.Root, 2, mode).ConfigureAwait(false))))
        .Step("Join", async context => context.Data.Result = await Work.Add(context.Data.Left + context.Data.Right, 0, mode).ConfigureAwait(false))
        .Build();
    public async Task<int> RunAsync(int input)
    {
        var state = new DiamondState { Input = input };
        await _workflow.ExecuteAsync(new WorkflowContext<DiamondState>(state)).ConfigureAwait(false);
        return state.Result;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    private sealed class Branch(string name, Func<IWorkflowContext<DiamondState>, Task> action) : WorkflowFramework.IStep<DiamondState>
    {
        public string Name => name;
        public Task ExecuteAsync(IWorkflowContext<DiamondState> context) => action(context);
    }
    private sealed class DiamondState { public int Input, Root, Left, Right, Result; }
}

