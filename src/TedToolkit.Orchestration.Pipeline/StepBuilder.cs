namespace TedToolkit.Orchestration.Pipeline;

/// <summary>A compile-time marker for a configured resultless step.</summary>
public readonly struct StepBuilder
{
    /// <summary>Adds a successful-completion dependency on a resultless node.</summary>
    /// <param name="dependency">A previously declared node.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder DependsOn(StepBuilder dependency) => default;

    /// <summary>Adds a successful-completion dependency on a result-bearing node.</summary>
    /// <typeparam name="TDependency">The dependency result type, which is not transported.</typeparam>
    /// <param name="dependency">A previously declared node.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder DependsOn<TDependency>(StepBuilder<TDependency> dependency) => default;

    /// <summary>Sets the retry count for this node.</summary>
    /// <param name="retryCount">A nonnegative compile-time constant.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder WithRetry(int retryCount) => default;

    /// <summary>Sets the timeout in milliseconds for this node.</summary>
    /// <param name="milliseconds">A positive compile-time constant, or -1 for infinite.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder WithTimeout(int milliseconds) => default;

    /// <summary>Overrides the display name supplied to this node.</summary>
    /// <param name="displayName">A nonempty compile-time constant.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder WithDisplayName(string displayName) => default;
}

/// <summary>A compile-time marker binding a downstream parameter to a step result.</summary>
/// <typeparam name="TResult">The result type.</typeparam>
public readonly struct StepBuilder<TResult>
{
    /// <summary>Adds a successful-completion dependency on a resultless node.</summary>
    /// <param name="dependency">A previously declared node.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder<TResult> DependsOn(StepBuilder dependency) => default;

    /// <summary>Adds a successful-completion dependency on a result-bearing node.</summary>
    /// <typeparam name="TDependency">The dependency result type, which is not transported.</typeparam>
    /// <param name="dependency">A previously declared node.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder<TResult> DependsOn<TDependency>(StepBuilder<TDependency> dependency) => default;

    /// <summary>Sets the retry count for this node.</summary>
    /// <param name="retryCount">A nonnegative compile-time constant.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder<TResult> WithRetry(int retryCount) => default;

    /// <summary>Sets the timeout in milliseconds for this node.</summary>
    /// <param name="milliseconds">A positive compile-time constant, or -1 for infinite.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder<TResult> WithTimeout(int milliseconds) => default;

    /// <summary>Overrides the display name supplied to this node.</summary>
    /// <param name="displayName">A nonempty compile-time constant.</param>
    /// <returns>This declaration marker.</returns>
    public StepBuilder<TResult> WithDisplayName(string displayName) => default;
}
