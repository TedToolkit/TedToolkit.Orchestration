namespace TedToolkit.Orchestration.Pipeline;

/// <summary>A compile-time argument: an expression, dependency, or omitted execution input.</summary>
/// <typeparam name="TValue">The step parameter type.</typeparam>
public readonly struct StepArgument<TValue>
{
    /// <summary>Marks an expression for execution-time evaluation by the generator.</summary>
    /// <param name="value">The expression whose source is mirrored.</param>
    public static implicit operator StepArgument<TValue>(TValue value) => default;

    /// <summary>Marks an upstream result dependency.</summary>
    /// <param name="source">The upstream step marker.</param>
    public static implicit operator StepArgument<TValue>(StepBuilder<TValue> source) => default;
}
