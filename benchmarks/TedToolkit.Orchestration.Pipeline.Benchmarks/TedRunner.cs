using TedToolkit.Orchestration.Pipeline.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

internal sealed class TedRunner : IRunner
{
    private readonly TedChain.ConfigurationPipeline? _chain;
    private readonly TedDiamond.ConfigurationPipeline? _diamond;
    private readonly WorkMode _mode;
    internal TedRunner(WorkMode mode, bool diamond)
    {
        _mode = mode;
        if (diamond) _diamond = new TedDiamond.ConfigurationPipeline();
        else _chain = new TedChain.ConfigurationPipeline();
    }
    public async Task<int> RunAsync(int input) => _diamond is not null
        ? (await _diamond.ExecuteAsync(input, _mode).ConfigureAwait(false)).Output
        : (await _chain!.ExecuteAsync(input, _mode).ConfigureAwait(false)).Output;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal static partial class TedChain
{
    [Pipeline]
    public static void Configuration(StepGraph p, int input, WorkMode mode)
    {
        var first = p.AddStep(input, 1, mode);
        var second = p.AddStep(first, 1, mode);
        var third = p.AddStep(second, 1, mode);
        var output = p.AddStep(third, 1, mode);
    }
}
internal static partial class TedDiamond
{
    [Pipeline]
    public static void Configuration(StepGraph p, int input, WorkMode mode)
    {
        var first = p.AddStep(input, 1, mode);
        var left = p.AddStep(first, 1, mode);
        var right = p.AddStep(first, 2, mode);
        var output = p.JoinStep(left, right, mode);
    }
}
internal static class TedSteps
{
    [Step]
    internal static Task<int> AddStep(
        int value, int amount, WorkMode mode, CancellationToken cancellationToken) =>
        Work.Add(value, amount, mode);

    [Step]
    internal static Task<int> JoinStep(
        int left, int right, WorkMode mode, CancellationToken cancellationToken) =>
        Work.Add(left + right, 0, mode);
}


