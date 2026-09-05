using System.Threading.Tasks.Dataflow;
namespace TedToolkit.Orchestration.Pipeline.Benchmarks;

internal sealed class DataflowRunner : IRunner
{
    private readonly TransformBlock<int, int> _input;
    private readonly ISourceBlock<int> _output;
    internal DataflowRunner(WorkMode mode, bool diamond)
    {
        var links = new DataflowLinkOptions { PropagateCompletion = true };
        _input = new TransformBlock<int, int>(value => Work.Add(value, 1, mode));
        if (diamond)
        {
            var broadcast = new BroadcastBlock<int>(null);
            var left = new TransformBlock<int, int>(value => Work.Add(value, 1, mode));
            var right = new TransformBlock<int, int>(value => Work.Add(value, 2, mode));
            var join = new JoinBlock<int, int>();
            var output = new TransformBlock<Tuple<int, int>, int>(values => Work.Add(values.Item1 + values.Item2, 0, mode));
            _input.LinkTo(broadcast, links);
            broadcast.LinkTo(left, links);
            broadcast.LinkTo(right, links);
            left.LinkTo(join.Target1, links);
            right.LinkTo(join.Target2, links);
            join.LinkTo(output, links);
            _output = output;
        }
        else
        {
            var second = new TransformBlock<int, int>(value => Work.Add(value, 1, mode));
            var third = new TransformBlock<int, int>(value => Work.Add(value, 1, mode));
            var output = new TransformBlock<int, int>(value => Work.Add(value, 1, mode));
            _input.LinkTo(second, links);
            second.LinkTo(third, links);
            third.LinkTo(output, links);
            _output = output;
        }
    }
    public async Task<int> RunAsync(int input)
    {
        // One message in flight: request latency, not stream throughput.
        if (!_input.Post(input)) throw new InvalidOperationException("The dataflow graph rejected its input.");
        return await _output.ReceiveAsync().ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        _input.Complete();
        await _output.Completion.ConfigureAwait(false);
    }
}

