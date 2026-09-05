namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Declares the fallback target used when no guarded transition accepts a trigger.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class TransitionOtherwiseToAttribute : Attribute
{
    /// <summary>Creates a fallback transition declaration.</summary>
    /// <param name="targetState">The fallback target state.</param>
    /// <param name="sourceStates">The source states to which the fallback applies.</param>
    public TransitionOtherwiseToAttribute(object targetState, params object[] sourceStates)
    {
        TargetState = targetState;
        SourceStates = sourceStates;
    }

    /// <summary>Gets the target state.</summary>
    public object TargetState { get; }

    /// <summary>Gets the permitted source states.</summary>
    public IReadOnlyList<object> SourceStates { get; }
}