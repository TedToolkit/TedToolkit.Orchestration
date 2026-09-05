namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Stores the current state for a generated state machine.</summary>
/// <typeparam name="TState">The state enum.</typeparam>
public class StateMachine<TState>
    where TState : struct, Enum
{
    private TState _state;
    private int _stateMachineCallbackDepth;

    /// <summary>Creates a state machine in the supplied state.</summary>
    /// <param name="initialState">The state exposed before the first transition.</param>
    public StateMachine(TState initialState)
    {
        _state = initialState;
    }

    /// <summary>Gets the current state.</summary>
    public TState State
    {
        get => _state;
        protected set => _state = value;
    }

    /// <summary>Occurs after the state is committed and before entry handlers run.</summary>
    public event StateTransitionEventHandler<TState>? Transitioned;

    /// <summary>Occurs after all entry handlers complete successfully.</summary>
    public event StateTransitionEventHandler<TState>? TransitionCompleted;

    /// <summary>Publishes a committed transition before entry handlers run.</summary>
    /// <param name="source">The state that was exited.</param>
    /// <param name="destination">The committed state.</param>
    protected void RaiseTransitioned(TState source, TState destination)
    {
        var handler = Transitioned;
        if (handler is null) return;
        using (EnterConsumerCallback())
        {
            handler(this, new StateTransition<TState>(source, destination));
        }
    }

    /// <summary>Publishes a transition after all entry handlers complete.</summary>
    /// <param name="source">The state that was exited.</param>
    /// <param name="destination">The committed state.</param>
    protected void RaiseTransitionCompleted(TState source, TState destination)
    {
        var handler = TransitionCompleted;
        if (handler is null) return;
        using (EnterConsumerCallback())
        {
            handler(this, new StateTransition<TState>(source, destination));
        }
    }

    /// <summary>Gets whether generated code is currently invoking consumer behavior.</summary>
    protected bool IsConsumerCallbackActive => _stateMachineCallbackDepth != 0;

    /// <summary>Enters generated invocation of consumer behavior.</summary>
    protected ConsumerCallbackScope EnterConsumerCallback()
    {
        _stateMachineCallbackDepth++;
        return new ConsumerCallbackScope(this);
    }

    /// <summary>Restores the callback depth when a generated consumer invocation ends.</summary>
    protected readonly struct ConsumerCallbackScope : IDisposable
    {
        private readonly StateMachine<TState> _machine;

        internal ConsumerCallbackScope(StateMachine<TState> machine)
        {
            _machine = machine;
        }

        /// <inheritdoc />
        public void Dispose() => _machine._stateMachineCallbackDepth--;
    }
}
