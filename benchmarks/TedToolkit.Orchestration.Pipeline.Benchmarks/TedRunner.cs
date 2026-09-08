using TedToolkit.Orchestration.Pipeline.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

internal sealed class TedRunner : IRunner
{
    private readonly TedChain.Pipeline? _chain;
    private readonly TedDiamond.Pipeline? _diamond;
    private readonly WorkMode _mode;
    internal TedRunner(WorkMode mode, bool diamond)
    {
        _mode = mode;
        if (diamond) _diamond = new TedDiamond.Pipeline();
        else _chain = new TedChain.Pipeline();
    }
    public async Task<int> RunAsync(int input) => _diamond is not null
        ? (await _diamond.ExecuteAsync(input, _mode).ConfigureAwait(false)).Output
        : (await _chain!.ExecuteAsync(input, _mode).ConfigureAwait(false)).Output;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
[CompositeStep]
internal readonly ref partial struct TedChain(int input, WorkMode mode)
{
    private void Configuration(StepGraph p)
    {
        var first = p.AddStep(input, 1, mode);
        var second = p.AddStep(first, 1, mode);
        var third = p.AddStep(second, 1, mode);
        var output = p.AddStep(third, 1, mode);
    }
}
[CompositeStep]
internal readonly ref partial struct TedDiamond(int input, WorkMode mode)
{
    private void Configuration(StepGraph p)
    {
        var first = p.AddStep(input, 1, mode);
        var left = p.AddStep(first, 1, mode);
        var right = p.AddStep(first, 2, mode);
        var output = p.JoinStep(left, right, mode);
    }
}
internal readonly ref partial struct AddStep(int value, int amount, WorkMode mode) : IAsyncStep<int>
{
    public Task<int> ExecuteAsync(CancellationToken cancellationToken) => Work.Add(value, amount, mode);
}
internal readonly ref partial struct JoinStep(int left, int right, WorkMode mode) : IAsyncStep<int>
{
    public Task<int> ExecuteAsync(CancellationToken cancellationToken) => Work.Add(left + right, 0, mode);
}


