using System.ComponentModel;

namespace TedToolkit.Orchestration.Pipeline.CompilerServices;

/// <summary>Identifies a generated Composite Step compiler protocol.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedCompositeStepAttribute : Attribute
{
    /// <summary>Initializes a generated Composite Step protocol marker.</summary>
    /// <param name="version">The compiler protocol version.</param>
    /// <param name="requiresServices">Whether execution requires a caller-owned service provider.</param>
    public GeneratedCompositeStepAttribute(int version, bool requiresServices)
    {
        Version = version;
        RequiresServices = requiresServices;
    }

    /// <summary>Gets the compiler protocol version.</summary>
    public int Version { get; }

    /// <summary>Gets whether execution requires a caller-owned service provider.</summary>
    public bool RequiresServices { get; }
}
