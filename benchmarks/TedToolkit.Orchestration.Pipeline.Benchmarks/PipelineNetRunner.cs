using PipelineNet.Middleware;
using PipelineNet.MiddlewareResolver;
using PipelineNet.Pipelines;
namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

internal sealed class PipelineNetRunner(WorkMode mode) : IRunner
{
    // The standard resolver creates fresh middleware for each invocation.
    private readonly IAsyncPipeline<PipelineNetInput> _pipeline = new AsyncPipeline<PipelineNetInput>(new ActivatorMiddlewareResolver())
        .Add<IncrementMiddleware>().Add<IncrementMiddleware>().Add<IncrementMiddleware>().Add<IncrementMiddleware>();
    public async Task<int> RunAsync(int input)
    {
        var state = new PipelineNetInput { Value = input, Mode = mode };
        await _pipeline.Execute(state).ConfigureAwait(false);
        return state.Value;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
public sealed class PipelineNetInput { public int Value; public WorkMode Mode; }
public sealed class IncrementMiddleware : IAsyncMiddleware<PipelineNetInput>
{
    public async Task Run(PipelineNetInput parameter, Func<PipelineNetInput, Task> next)
    {
        parameter.Value = await Work.Add(parameter.Value, 1, parameter.Mode).ConfigureAwait(false);
        await next(parameter).ConfigureAwait(false);
    }
}

