namespace TedToolkit.Orchestration.Pipeline.Attributes;

/// <summary>Configures compile-time execution policies for a step.</summary>
/// <remarks>Each retry constructs a fresh step from the same captured arguments. Timeout is cooperative.</remarks>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class StepPolicyAttribute : Attribute
{
    /// <summary>Gets or sets additional attempts after failure. Defaults to zero.</summary>
    public int RetryCount { get; set; }

    /// <summary>Gets or sets the per-attempt timeout in milliseconds. Use -1 for infinite; otherwise a positive value.</summary>
    public int TimeoutMilliseconds { get; set; } = -1;
}
