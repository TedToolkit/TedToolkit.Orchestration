namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Marks a method that runs whenever a transition enters one of the specified states.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnEntryAttribute : Attribute
{
    /// <summary>Creates an entry lifecycle declaration.</summary>
    /// <param name="states">The states whose entry invokes the attributed method.</param>
    public OnEntryAttribute(params object[] states)
    {
        States = states;
    }

    /// <summary>Gets the states whose entry invokes the attributed method.</summary>
    public IReadOnlyList<object> States { get; }
}