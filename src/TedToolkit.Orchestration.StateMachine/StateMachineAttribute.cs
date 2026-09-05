namespace TedToolkit.Orchestration.StateMachine;

/// <summary>Declares a partial class whose state machine base and triggers are generated at compilation.</summary>
/// <typeparam name="TState">The enum used as the machine state.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StateMachineAttribute<TState> : Attribute
    where TState : struct, Enum
{
    /// <summary>Initializes the attribute without declaring a default initial state.</summary>
    public StateMachineAttribute()
    {
    }

    /// <summary>Initializes the attribute with the state used by the generated parameterless constructor.</summary>
    /// <param name="initialState">The default initial state.</param>
    public StateMachineAttribute(TState initialState)
    {
    }
}
