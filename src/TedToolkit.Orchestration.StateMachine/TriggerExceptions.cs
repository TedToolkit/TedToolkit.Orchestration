namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Thrown by a strict generated trigger when its expected transition is rejected.</summary>
public sealed class TriggerRejectedException : InvalidOperationException
{
    /// <summary>Creates a trigger rejection exception.</summary>
    public TriggerRejectedException(string trigger, object state, TriggerRejection rejection)
        : base($"Trigger '{trigger}' was rejected in state '{state}' ({rejection}).")
    {
        Trigger = trigger;
        State = state;
        Rejection = rejection;
    }

    /// <summary>Gets the trigger method name.</summary>
    public string Trigger { get; }

    /// <summary>Gets the state in which the trigger was rejected.</summary>
    public object State { get; }

    /// <summary>Gets the rejection reason.</summary>
    public TriggerRejection Rejection { get; }
}

/// <summary>Thrown when more than one guarded target accepts the same trigger invocation.</summary>
public sealed class AmbiguousTransitionException : InvalidOperationException
{
    /// <summary>Creates an ambiguous transition exception.</summary>
    public AmbiguousTransitionException(string trigger, object state)
        : base($"Trigger '{trigger}' has multiple matching transitions in state '{state}'. Guards must be mutually exclusive.")
    {
        Trigger = trigger;
        State = state;
    }

    /// <summary>Gets the trigger method name.</summary>
    public string Trigger { get; }

    /// <summary>Gets the source state.</summary>
    public object State { get; }
}

/// <summary>Thrown when a trigger is invoked from active state-machine callback behavior.</summary>
public sealed class ReentrantTriggerException : InvalidOperationException
{
    /// <summary>Creates a reentrant trigger exception.</summary>
    public ReentrantTriggerException(string trigger, object state)
        : base($"Trigger '{trigger}' cannot run reentrantly from a guard, lifecycle handler, or transition event in state '{state}'.")
    {
        Trigger = trigger;
        State = state;
    }

    /// <summary>Gets the reentrant trigger method name.</summary>
    public string Trigger { get; }

    /// <summary>Gets the state observed by the reentrant invocation.</summary>
    public object State { get; }
}
