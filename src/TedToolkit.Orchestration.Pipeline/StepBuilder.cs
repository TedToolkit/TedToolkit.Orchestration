namespace TedToolkit.Orchestration.Pipeline;

/// <summary>A compile-time marker for a configured resultless step.</summary>
public readonly struct StepBuilder;

/// <summary>A compile-time marker binding a downstream parameter to a step result.</summary>
/// <typeparam name="TResult">The result type.</typeparam>
public readonly struct StepBuilder<TResult>;
