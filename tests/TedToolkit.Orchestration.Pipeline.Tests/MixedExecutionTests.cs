namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task MixedSerialStepsWaitInOrderAndDisposeTheSynchronousResultStep(bool discard)
    {
        var result = await Run("""
            public sealed class State { public string Order = ""; public int Last; public int Disposed; }
            internal readonly ref struct Seed(int value, State state) : IStep<int>
            {
                public int Execute(CancellationToken token) { state.Order += "S"; return value + 1; }
            }
            internal readonly ref struct Wait(int value, Task gate, State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => Run(value, gate, state);
                private static async Task<int> Run(int value, Task gate, State state)
                {
                    state.Order += "A"; await gate; state.Order += "a"; return value * 2;
                }
            }
            internal readonly ref struct Finish(int value, State state) : IStep<int>, IDisposable
            {
                public int Execute(CancellationToken token) { state.Order += "F"; state.Last = value + 3; return state.Last; }
                public void Dispose() { state.Order += "D"; state.Disposed++; }
            }
            public sealed partial class Example : Pipeline
            {
                private void Configure(Builder p, State state)
                {
                    var seed = p.Seed(state: state);
                    var wait = p.Wait(seed, state: state);
                    var finish = p.Finish(wait, state);
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var state = new State();
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var pipeline = new Example(services, state);
                Task pending = pipeline.METHOD(seedValue: 4, waitGate: gate.Task);
                var before = state.Order;
                var early = pending.IsCompleted;
                gate.SetResult();
                await pending;
                var first = state.Last;
                var second = await pipeline.ExecuteAsync(seedValue: 9, waitGate: Task.CompletedTask);
                return $"{before}:{early}:{first}:{second.Finish}:{state.Disposed}:{state.Order}";
                """.Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")));
        await Assert.That(result).IsEqualTo("SA:False:13:23:2:SAaFDSAaFD");
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task MixedDiamondKeepsIndependentReadinessAcrossSyncAndAsyncSteps(bool discard)
    {
        var result = await Run("""
            public sealed class State { public int Roots; public int Joined; public int Stored; }
            internal readonly ref struct Root(int value, State state) : IStep<int>
            {
                public int Execute(CancellationToken token) { state.Roots++; return value + 1; }
            }
            internal readonly ref struct Slow(int value, Task gate) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => Run(value, gate);
                private static async Task<int> Run(int value, Task gate) { await gate; return value + 2; }
            }
            internal readonly ref struct Fast(int value) : IStep<int>
            {
                public int Execute(CancellationToken token) => value + 3;
            }
            internal readonly ref struct Child(int value, TaskCompletionSource signal) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) { signal.SetResult(); return Task.FromResult(value + 4); }
            }
            internal readonly ref struct Join(int left, int right, State state) : IStep<int>
            {
                public int Execute(CancellationToken token) { state.Joined++; return left + right; }
            }
            internal readonly ref struct Store(int value, State state) : IStep
            {
                public void Execute(CancellationToken token) => state.Stored = value;
            }
            public sealed partial class Example : Pipeline
            {
                private void Configure(Builder p, State state)
                {
                    var root = p.Root(state: state);
                    var slow = p.Slow(root);
                    var fast = p.Fast(root);
                    var child = p.Child(fast);
                    var join = p.Join(slow, child, state);
                    p.Store(join, state);
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var state = new State();
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task pending = new Example(services, state).METHOD(rootValue: 10, slowGate: release.Task, childSignal: signal);
                bool premature;
                try
                {
                    await signal.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    premature = pending.IsCompleted || state.Joined != 0;
                }
                finally { release.TrySetResult(); }
                await pending;
                return $"{premature}:{state.Roots}:{state.Joined}:{state.Stored}";
                """.Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")));
        await Assert.That(result).IsEqualTo("False:1:1:31");
    }

    [Test]
    public async Task ConcurrentFailuresPreserveAnOriginalExceptionAndDrainBothSteps()
    {
        var result = await Run(ConcurrentSteps + """
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { var later = p.Start(); var first = p.Start(); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var finished = 0;
                var firstFailure = new InvalidOperationException("first observed");
                var laterFailure = new InvalidOperationException("later observed");
                var pending = new Example(services).ExecuteAsync(
                    laterWork: async token =>
                    {
                        using var registration = token.Register(() => canceled.TrySetResult());
                        await release.Task;
                        Interlocked.Increment(ref finished);
                        throw laterFailure;
                    },
                    firstWork: token => { Interlocked.Increment(ref finished); return Task.FromException<int>(firstFailure); });
                try { await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                finally { release.TrySetResult(); }
                try { await pending; return "unexpected"; }
                catch (Exception actual)
                {
                    return $"{ReferenceEquals(actual, firstFailure) || ReferenceEquals(actual, laterFailure)}:{finished == 2}";
                }
                """));
        await Assert.That(result).IsEqualTo("True:True");
    }
}
