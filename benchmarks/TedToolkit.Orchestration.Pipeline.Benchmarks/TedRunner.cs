namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

internal sealed class TedRunner : IRunner
{
    private readonly TedChain? _chain;
    private readonly TedDiamond? _diamond;
    internal TedRunner(WorkMode mode, bool diamond)
    {
        if (diamond) _diamond = new TedDiamond(EmptyServices.Instance, mode);
        else _chain = new TedChain(EmptyServices.Instance, mode);
    }
    public async Task<int> RunAsync(int input) => _diamond is not null
        ? (await _diamond.ExecuteAsync(input).ConfigureAwait(false)).Output
        : (await _chain!.ExecuteAsync(input).ConfigureAwait(false)).Output;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal partial class TedChain : global::TedToolkit.Orchestration.Pipeline.Pipeline
{
    private void Configure(Builder p, WorkMode mode)
    {
        var first = p.AddStep(amount: 1, mode: mode);
        var second = p.AddStep(first, 1, mode);
        var third = p.AddStep(second, 1, mode);
        var output = p.AddStep(third, 1, mode);
    }
}
internal partial class TedDiamond : global::TedToolkit.Orchestration.Pipeline.Pipeline
{
    private void Configure(Builder p, WorkMode mode)
    {
        var first = p.AddStep(amount: 1, mode: mode);
        var left = p.AddStep(first, 1, mode);
        var right = p.AddStep(first, 2, mode);
        var output = p.JoinStep(left, right, mode);
    }
}
internal readonly ref struct AddStep(int value, int amount, WorkMode mode) : IAsyncStep<int>
{
    public Task<int> ExecuteAsync(CancellationToken cancellationToken) => Work.Add(value, amount, mode);
}
internal readonly ref struct JoinStep(int left, int right, WorkMode mode) : IAsyncStep<int>
{
    public Task<int> ExecuteAsync(CancellationToken cancellationToken) => Work.Add(left + right, 0, mode);
}
internal sealed class EmptyServices : IServiceProvider
{
    internal static readonly EmptyServices Instance = new();
    public object? GetService(Type serviceType) => null;
}


