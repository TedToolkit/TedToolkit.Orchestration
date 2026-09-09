#if NETSTANDARD2_0 || NETSTANDARD2_1 || NET472 || NET48
namespace System.Runtime.CompilerServices
{
    /// <summary>Supports init-only members emitted into legacy Pipeline consumers.</summary>
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    [global::System.Runtime.CompilerServices.CompilerGenerated]
    public static class IsExternalInit { }
}
#endif

#if NETSTANDARD2_0 || NETSTANDARD2_1 || NET472 || NET48 || NET6_0
namespace System.Runtime.CompilerServices
{
    /// <summary>Marks required members emitted into legacy Pipeline consumers.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class |
        global::System.AttributeTargets.Struct | global::System.AttributeTargets.Field |
        global::System.AttributeTargets.Property, Inherited = false)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    [global::System.Runtime.CompilerServices.CompilerGenerated]
    public sealed class RequiredMemberAttribute : global::System.Attribute { }

    /// <summary>Marks compiler features used by generated legacy Pipeline consumers.</summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.All, Inherited = false)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    [global::System.Runtime.CompilerServices.CompilerGenerated]
    public sealed class CompilerFeatureRequiredAttribute(string featureName) : global::System.Attribute
    {
        /// <summary>Gets the compiler feature name.</summary>
        public string FeatureName { get; } = featureName;
    }
}
#endif
