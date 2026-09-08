namespace TedToolkit.Orchestration.Pipeline.Attributes;

/// <summary>Requests a generated ILogger property initialized for each configured Step invocation.</summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class StepLoggerAttribute : Attribute;
