namespace TedToolkit.Orchestration.Pipeline.Attributes;

/// <summary>Marks a readonly ref partial struct whose static graph is declared by Configuration(StepGraph).</summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class CompositeStepAttribute : Attribute;
