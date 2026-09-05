namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Allows the attributed trigger to transition from one or more source states to a target state.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class TransitionToAttribute : Attribute
{
    /// <summary>Creates a transition declaration.</summary>
    /// <param name="targetState">The state entered after the trigger succeeds.</param>
    /// <param name="sourceStates">The states from which the trigger is permitted.</param>
    public TransitionToAttribute(object targetState, params object[] sourceStates)
    {
        TargetState = targetState;
        SourceStates = sourceStates;
    }

    /// <summary>Gets the target state.</summary>
    public object TargetState { get; }

    /// <summary>Gets the permitted source states.</summary>
    public IReadOnlyList<object> SourceStates { get; }

    /// <summary>Gets or sets the optional guard method name for this candidate transition.</summary>
    public string? Guard { get; set; }
}