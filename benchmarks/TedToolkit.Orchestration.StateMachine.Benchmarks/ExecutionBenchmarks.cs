using BenchmarkDotNet.Attributes;

namespace TedToolkit.Orchestration.StateMachine.Benchmarks;

[MemoryDiagnoser]
public class TransitionBenchmarks
{
    private readonly HandwrittenMachine _handwritten = new(BenchmarkState.A);
    private readonly TedBasicMachine _ted = new(BenchmarkState.A);
    private readonly StatelessBasicMachine _stateless = new();
    private readonly Appccelerate.StateMachine.PassiveStateMachine<BenchmarkState, BenchmarkTrigger> _appccelerate =
        AppccelerateMachines.CreateBasic();

    [Benchmark(Baseline = true)]
    public int Handwritten() => (int)_handwritten.Toggle();

    [Benchmark]
    public int TedToolkit() => (int)_ted.Toggle();

    [Benchmark]
    public int Stateless() => (int)_stateless.Toggle();

    [Benchmark]
    public object Appccelerate()
    {
        _appccelerate.Fire(BenchmarkTrigger.Toggle);
        return _appccelerate;
    }
}

[MemoryDiagnoser]
public class GuardAndLifecycleBenchmarks
{
    private readonly HandwrittenMachine _handwritten = new(BenchmarkState.A);
    private readonly TedGuardedMachine _ted = new(BenchmarkState.A);
    private readonly StatelessGuardedMachine _stateless = new();
    private readonly AppccelerateGuardedMachine _appccelerate = AppccelerateMachines.CreateGuarded();

    [Benchmark(Baseline = true)]
    public int Handwritten() => _handwritten.GuardedToggle();

    [Benchmark]
    public int TedToolkit() => _ted.Toggle();

    [Benchmark]
    public int Stateless() => _stateless.Toggle();

    [Benchmark]
    public int Appccelerate() => _appccelerate.Toggle();
}

[MemoryDiagnoser]
public class CanFireBenchmarks
{
    private readonly HandwrittenMachine _handwritten = new(BenchmarkState.A);
    private readonly TedBasicMachine _ted = new(BenchmarkState.A);
    private readonly StatelessBasicMachine _stateless = new();

    [Benchmark(Baseline = true)]
    public bool Handwritten() => _handwritten.CanToggle();

    [Benchmark]
    public bool TedToolkit() => _ted.CanToggleAsync().GetAwaiter().GetResult();

    [Benchmark]
    public bool Stateless() => _stateless.Machine.CanFire(BenchmarkTrigger.Toggle);
}

[MemoryDiagnoser]
public class ConstructionBenchmarks
{
    [Benchmark(Baseline = true)]
    public object Handwritten() => new HandwrittenMachine(BenchmarkState.A);

    [Benchmark]
    public object TedToolkit() => new TedBasicMachine(BenchmarkState.A);

    [Benchmark]
    public object Stateless() => new StatelessBasicMachine();

    [Benchmark]
    public object Appccelerate()
        => AppccelerateMachines.CreateBasic();
}
