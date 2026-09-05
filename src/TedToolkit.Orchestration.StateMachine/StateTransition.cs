namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Describes a committed state transition.</summary>
/// <typeparam name="TState">The state enum.</typeparam>
public readonly record struct StateTransition<TState>(TState Source, TState Destination)
    where TState : struct, Enum;

/// <summary>Handles a state transition notification.</summary>
/// <typeparam name="TState">The state enum.</typeparam>
/// <param name="machine">The machine that committed the transition.</param>
/// <param name="transition">The source and destination states.</param>
public delegate void StateTransitionEventHandler<TState>(
    StateMachine<TState> machine,
    StateTransition<TState> transition)
    where TState : struct, Enum;