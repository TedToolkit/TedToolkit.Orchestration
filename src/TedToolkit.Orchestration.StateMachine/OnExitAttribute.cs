namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Marks a method that runs whenever a transition exits one of the specified states.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnExitAttribute : Attribute
{
    /// <summary>Creates an exit lifecycle declaration.</summary>
    /// <param name="states">The states whose exit invokes the attributed method.</param>
    public OnExitAttribute(params object[] states)
    {
        States = states;
    }

    /// <summary>Gets the states whose exit invokes the attributed method.</summary>
    public IReadOnlyList<object> States { get; }
}