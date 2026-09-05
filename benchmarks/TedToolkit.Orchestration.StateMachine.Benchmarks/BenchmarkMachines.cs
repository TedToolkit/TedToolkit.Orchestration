using Appccelerate.StateMachine;
using Appccelerate.StateMachine.Machine;

using TedToolkit.Orchestration.StateMachine;

namespace TedToolkit.Orchestration.StateMachine.Benchmarks;

internal enum BenchmarkState
{
    A,
    B,
}

internal enum BenchmarkTrigger
{
    Toggle,
}

[StateMachine<BenchmarkState>]
internal sealed partial class TedBasicMachine
{
    [TransitionTo(BenchmarkState.B, BenchmarkState.A)]
    [TransitionTo(BenchmarkState.A, BenchmarkState.B)]
    public partial ValueTask ToggleAsync();

    internal BenchmarkState Toggle()
    {
        ToggleAsync().GetAwaiter().GetResult();
        return State;
    }
}

[StateMachine<BenchmarkState>]
internal sealed partial class TedGuardedMachine
{
    internal int ActionCount { get; private set; }

    [TransitionTo(BenchmarkState.B, BenchmarkState.A)]
    [TransitionTo(BenchmarkState.A, BenchmarkState.B)]
    public partial ValueTask ToggleAsync(bool allowed);

    private static bool CanToggle(bool allowed) => allowed;

    [OnExit(BenchmarkState.A, BenchmarkState.B)]
    private void Exit() => ActionCount++;

    [OnEntry(BenchmarkState.A, BenchmarkState.B)]
    private void Enter() => ActionCount++;

    internal int Toggle()
    {
        ToggleAsync(true).GetAwaiter().GetResult();
        return ActionCount;
    }
}

internal sealed class HandwrittenMachine(BenchmarkState initialState)
{
    internal BenchmarkState State { get; private set; } = initialState;

    internal int ActionCount { get; private set; }

    internal bool CanToggle() => true;

    internal BenchmarkState Toggle()
    {
        State = State == BenchmarkState.A ? BenchmarkState.B : BenchmarkState.A;
        return State;
    }

    internal int GuardedToggle()
    {
        ActionCount++;
        State = State == BenchmarkState.A ? BenchmarkState.B : BenchmarkState.A;
        ActionCount++;
        return ActionCount;
    }
}

internal sealed class StatelessBasicMachine
{
    internal StatelessBasicMachine()
    {
        Machine.Configure(BenchmarkState.A).Permit(BenchmarkTrigger.Toggle, BenchmarkState.B);
        Machine.Configure(BenchmarkState.B).Permit(BenchmarkTrigger.Toggle, BenchmarkState.A);
    }

    internal Stateless.StateMachine<BenchmarkState, BenchmarkTrigger> Machine { get; } =
        new(BenchmarkState.A);

    internal BenchmarkState Toggle()
    {
        Machine.Fire(BenchmarkTrigger.Toggle);
        return Machine.State;
    }
}

internal sealed class StatelessGuardedMachine
{
    internal StatelessGuardedMachine()
    {
        Machine.Configure(BenchmarkState.A)
            .OnEntry(CountAction)
            .OnExit(CountAction)
            .PermitIf(BenchmarkTrigger.Toggle, BenchmarkState.B, static () => true);
        Machine.Configure(BenchmarkState.B)
            .OnEntry(CountAction)
            .OnExit(CountAction)
            .PermitIf(BenchmarkTrigger.Toggle, BenchmarkState.A, static () => true);
    }

    internal Stateless.StateMachine<BenchmarkState, BenchmarkTrigger> Machine { get; } =
        new(BenchmarkState.A);

    internal int ActionCount { get; private set; }

    internal int Toggle()
    {
        Machine.Fire(BenchmarkTrigger.Toggle);
        return ActionCount;
    }

    private void CountAction() => ActionCount++;
}

internal static class AppccelerateMachines
{
    internal static readonly StateMachineDefinition<BenchmarkState, BenchmarkTrigger> BasicDefinition =
        CreateBasicDefinition();

    internal static PassiveStateMachine<BenchmarkState, BenchmarkTrigger> CreateBasic()
    {
        var machine = BasicDefinition.CreatePassiveStateMachine();
        machine.Start();
        return machine;
    }

    internal static AppccelerateGuardedMachine CreateGuarded() => new();

    private static StateMachineDefinition<BenchmarkState, BenchmarkTrigger> CreateBasicDefinition()
    {
        var builder = new StateMachineDefinitionBuilder<BenchmarkState, BenchmarkTrigger>();
        builder.In(BenchmarkState.A).On(BenchmarkTrigger.Toggle).Goto(BenchmarkState.B);
        builder.In(BenchmarkState.B).On(BenchmarkTrigger.Toggle).Goto(BenchmarkState.A);
        builder.WithInitialState(BenchmarkState.A);
        return builder.Build();
    }
}

internal sealed class AppccelerateGuardedMachine
{
    private readonly PassiveStateMachine<BenchmarkState, BenchmarkTrigger> _machine;

    internal AppccelerateGuardedMachine()
    {
        var builder = new StateMachineDefinitionBuilder<BenchmarkState, BenchmarkTrigger>();
        builder.In(BenchmarkState.A).ExecuteOnEntry(CountAction);
        builder.In(BenchmarkState.A).ExecuteOnExit(CountAction);
        builder.In(BenchmarkState.A).On(BenchmarkTrigger.Toggle).If(static () => true).Goto(BenchmarkState.B);
        builder.In(BenchmarkState.B).ExecuteOnEntry(CountAction);
        builder.In(BenchmarkState.B).ExecuteOnExit(CountAction);
        builder.In(BenchmarkState.B).On(BenchmarkTrigger.Toggle).If(static () => true).Goto(BenchmarkState.A);
        builder.WithInitialState(BenchmarkState.A);
        _machine = builder.Build().CreatePassiveStateMachine();
        _machine.Start();
        ActionCount = 0;
    }

    internal int ActionCount { get; private set; }

    internal int Toggle()
    {
        _machine.Fire(BenchmarkTrigger.Toggle);
        return ActionCount;
    }

    private void CountAction() => ActionCount++;
}
