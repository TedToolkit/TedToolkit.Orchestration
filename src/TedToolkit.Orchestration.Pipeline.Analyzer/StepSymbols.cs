using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class StepSymbols
{
    internal const string STEP_ATTRIBUTE_NAME =
        "TedToolkit.Orchestration.Pipeline.Attributes.StepAttribute";
    internal const string PIPELINE_ATTRIBUTE_NAME =
        "TedToolkit.Orchestration.Pipeline.Attributes.PipelineAttribute";
    internal const string VOID_BUILDER_NAME = "TedToolkit.Orchestration.Pipeline.StepBuilder";
    internal const string SERVICE_NAME =
        "TedToolkit.Orchestration.Pipeline.Attributes.FromServicesAttribute";
    internal const string ARGUMENT_NAME = "TedToolkit.Orchestration.Pipeline.StepArgument`1";
    internal const string BUILDER_NAME = "TedToolkit.Orchestration.Pipeline.StepBuilder`1";

    internal static bool IsLeafStep(IMethodSymbol method) =>
        HasAttribute(method, STEP_ATTRIBUTE_NAME);

    internal static bool IsPipeline(IMethodSymbol method) =>
        HasAttribute(method, PIPELINE_ATTRIBUTE_NAME);

    internal static AttributeData? PipelineAttribute(IMethodSymbol method) =>
        method.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == PIPELINE_ATTRIBUTE_NAME);

    internal static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == metadataName);

    internal static bool IsUserAuthored(ISymbol symbol) => symbol.Locations.Any(location =>
        location.IsInSource &&
        location.SourceTree?.FilePath.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase) != true);

    internal static string? InvalidContract(IMethodSymbol method, Compilation compilation)
    {
        var owner = method.ContainingType;
        if (owner.TypeKind != TypeKind.Class || !owner.IsStatic ||
            owner.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Public) ||
            owner.IsFileLocal || owner.ContainingType is not null || owner.Arity != 0)
            return "Step methods must be declared in a top-level, non-generic internal or public static class";
        if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic ||
            method.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Public) ||
            method.IsGenericMethod)
            return "Step methods must be internal or public static non-generic methods";
        if (owner.GetMembers(method.Name).OfType<IMethodSymbol>()
            .Count(candidate => candidate.MethodKind == MethodKind.Ordinary) != 1)
            return "Step method names must be unique within their containing type";
        if (method.RefKind != RefKind.None || method.IsAsync && method.ReturnsVoid ||
            !TryGetReturnShape(method, out _, out _))
            return "Step methods must return void, a non-ref-like result, Task, or Task<TResult>";

        var cancellation = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        var tokens = method.Parameters.Where(parameter =>
            cancellation is not null &&
            SymbolEqualityComparer.Default.Equals(parameter.Type, cancellation)).ToArray();
        if (tokens.Length != 1 || tokens[0].Ordinal != method.Parameters.Length - 1 ||
            IsService(tokens[0]))
            return "Step methods must declare exactly one unmarked trailing CancellationToken";

        if (method.Parameters.Any(parameter =>
            parameter.RefKind != RefKind.None ||
            parameter.Type.IsRefLikeType ||
            parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
            HasTypeParameter(parameter.Type)))
            return "Step parameters must be closed by-value types and cannot be ref-like, pointer, or function-pointer types";

        var logger = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        if (method.Parameters.Any(parameter =>
            logger is not null &&
            SymbolEqualityComparer.Default.Equals(parameter.Type, logger) &&
            ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value is string))
            return "a non-generic ILogger service cannot use a key";
        return null;
    }

    internal static bool TryGetReturnShape(
        IMethodSymbol method, out ITypeSymbol? result, out bool synchronous)
    {
        result = null;
        synchronous = true;
        if (method.ReturnsVoid) return true;
        if (method.ReturnType.IsRefLikeType ||
            method.ReturnType.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
            HasTypeParameter(method.ReturnType))
            return false;
        if (method.ReturnType is INamedTypeSymbol named)
        {
            var definition = named.OriginalDefinition.ToDisplayString();
            if (definition is "System.Threading.Tasks.ValueTask" or
                "System.Threading.Tasks.ValueTask<TResult>")
                return false;
            if (definition == "System.Threading.Tasks.Task")
            {
                synchronous = false;
                return true;
            }
            if (definition == "System.Threading.Tasks.Task<TResult>")
            {
                result = named.TypeArguments[0];
                synchronous = false;
                return !result.IsRefLikeType && !HasTypeParameter(result);
            }
        }
        result = method.ReturnType;
        return true;
    }

    internal static bool IsCancellationToken(IParameterSymbol parameter) =>
        parameter.Type.ToDisplayString() == "System.Threading.CancellationToken";

    internal static bool IsService(IParameterSymbol parameter) =>
        ServiceAttribute(parameter) is not null;

    internal static AttributeData? ServiceAttribute(IParameterSymbol parameter) =>
        parameter.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == SERVICE_NAME);

    internal static bool SameType(ITypeSymbol left, ITypeSymbol right) =>
        SymbolEqualityComparer.IncludeNullability.Equals(left, right);

    internal static bool HasTypeParameter(ITypeSymbol type) => type.TypeKind == TypeKind.TypeParameter ||
        type is IArrayTypeSymbol array && HasTypeParameter(array.ElementType) ||
        type is INamedTypeSymbol named && (named.IsUnboundGenericType ||
            named.TypeArguments.Any(HasTypeParameter) ||
            named.ContainingType is not null && HasTypeParameter(named.ContainingType));

    private static readonly SymbolDisplayFormat _typeDisplayFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static string TypeName(ITypeSymbol type) => type.ToDisplayString(_typeDisplayFormat);

    internal static string TypeName(ITypeSymbol type, IAssemblySymbol assembly, string alias)
    {
        var parts = type.ToDisplayParts(_typeDisplayFormat);
        var result = new StringBuilder();
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].ToString() == "global" && index + 1 < parts.Length &&
                parts[index + 1].ToString() == "::" &&
                QualifierTargets(parts, index + 2, assembly))
                result.Append(alias);
            else
                result.Append(parts[index].ToString());
        }
        return result.ToString();
    }

    private static bool QualifierTargets(
        System.Collections.Immutable.ImmutableArray<SymbolDisplayPart> parts,
        int start,
        IAssemblySymbol assembly)
    {
        for (var index = start; index < parts.Length; index++)
        {
            var containingAssembly = parts[index].Symbol?.ContainingAssembly;
            if (containingAssembly is not null)
                return SymbolEqualityComparer.Default.Equals(containingAssembly, assembly);
        }
        return false;
    }
}
