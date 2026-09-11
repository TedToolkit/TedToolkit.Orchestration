namespace TedToolkit.Orchestration.Pipeline.Attributes;

/// <summary>Marks a static method as a leaf Step declaration.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class StepAttribute : Attribute;
