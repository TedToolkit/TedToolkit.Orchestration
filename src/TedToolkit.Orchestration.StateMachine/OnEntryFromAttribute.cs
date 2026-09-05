namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Marks a method that runs when a state is entered through one of the specified triggers.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OnEntryFromAttribute : Attribute
{
    /// <summary>Creates a trigger-specific entry lifecycle declaration.</summary>
    /// <param name="state">The state being entered.</param>
    /// <param name="triggers">The trigger method names, normally supplied with <see langword="nameof"/>.</param>
    public OnEntryFromAttribute(object state, params string[] triggers)
    {
        State = state;
        Triggers = triggers;
    }

    /// <summary>Gets the state being entered.</summary>
    public object State { get; }

    /// <summary>Gets the trigger method names that invoke the attributed method.</summary>
    public IReadOnlyList<string> Triggers { get; }
}