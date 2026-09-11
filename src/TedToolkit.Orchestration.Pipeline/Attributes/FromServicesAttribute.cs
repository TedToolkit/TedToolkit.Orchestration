namespace TedToolkit.Orchestration.Pipeline.Attributes;

/// <summary>Resolves a Step function parameter from the caller-owned DI scope.</summary>
/// <param name="key">The service key, or <see langword="null"/> to resolve an ordinary service.</param>
/// <remarks>Service parameters are omitted from generated builder methods. An empty string is a valid service key.</remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromServicesAttribute(string? key = null) : Attribute
{
    /// <summary>Gets the service key, or <see langword="null"/> for an ordinary service.</summary>
    public string? Key { get; } = key;
}
