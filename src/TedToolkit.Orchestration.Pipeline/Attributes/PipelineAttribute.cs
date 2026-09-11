namespace TedToolkit.Orchestration.Pipeline.Attributes;

/// <summary>Generates a reusable root execution facade for a Step function.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PipelineAttribute : Attribute
{
    /// <summary>Gets or sets the generated type-name stem. The generator appends <c>Pipeline</c>.</summary>
    public string? Name { get; set; }
}
