namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Identifies an expected reason why a trigger was not executed.</summary>
public enum TriggerRejection
{
    /// <summary>The current state does not declare this trigger.</summary>
    NotPermitted,

    /// <summary>The current state declares this trigger, but none of its guards accepted it.</summary>
    GuardRejected,
}

/// <summary>Describes the result of attempting a generated trigger without throwing for an expected rejection.</summary>
/// <typeparam name="TState">The state enum.</typeparam>
public readonly record struct TriggerResult<TState>
    where TState : struct, Enum
{
    private TriggerResult(bool succeeded, TState source, TState destination, TriggerRejection? rejection)
    {
        Succeeded = succeeded;
        Source = source;
        Destination = destination;
        Rejection = rejection;
    }

    /// <summary>Gets whether the transition was committed.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the state observed when the trigger began.</summary>
    public TState Source { get; }

    /// <summary>Gets the committed target, or the unchanged source when rejected.</summary>
    public TState Destination { get; }

    /// <summary>Gets the rejection reason, or null after success.</summary>
    public TriggerRejection? Rejection { get; }

    /// <summary>Creates a successful result.</summary>
    public static TriggerResult<TState> Success(TState source, TState destination) =>
        new(true, source, destination, null);

    /// <summary>Creates an expected rejected result.</summary>
    public static TriggerResult<TState> Rejected(TState source, TriggerRejection rejection) =>
        new(false, source, source, rejection);
}