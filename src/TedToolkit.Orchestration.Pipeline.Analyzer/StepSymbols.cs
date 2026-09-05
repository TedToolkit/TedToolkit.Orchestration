using System.Linq;
using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class StepSymbols
{
    internal const string AsyncVoidStepName = "TedToolkit.Orchestration.Pipeline.IAsyncStep";
    internal const string VoidBuilderName = "TedToolkit.Orchestration.Pipeline.StepBuilder";
    internal const string AsyncStepName = "TedToolkit.Orchestration.Pipeline.IAsyncStep`1";
    internal const string ServiceName = "TedToolkit.Orchestration.Pipeline.Attributes.FromServicesAttribute";
    internal const string ArgumentName = "TedToolkit.Orchestration.Pipeline.StepArgument`1";
    internal const string BuilderName = "TedToolkit.Orchestration.Pipeline.StepBuilder`1";

    internal const string VoidStepName = "TedToolkit.Orchestration.Pipeline.IStep";
    internal const string StepName = "TedToolkit.Orchestration.Pipeline.IStep`1";
    internal const string PolicyName = "TedToolkit.Orchestration.Pipeline.Attributes.StepPolicyAttribute";

    internal static INamedTypeSymbol[] Contracts(ITypeSymbol type, Compilation compilation)
    {
        var definitions = new[] { VoidStepName, StepName, AsyncVoidStepName, AsyncStepName }
            .Select(compilation.GetTypeByMetadataName).ToArray();
        return type.AllInterfaces.Where(contract => definitions.Any(definition =>
            SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, definition))).ToArray();
    }

    internal static ITypeSymbol? ResultType(ITypeSymbol type, Compilation compilation) =>
        Contracts(type, compilation).FirstOrDefault(contract => contract.TypeArguments.Length == 1)?.TypeArguments[0];

    internal static bool IsSynchronous(ITypeSymbol type, Compilation compilation) =>
        Contracts(type, compilation).Any(contract => contract.Name == "IStep");

    internal static bool IsStep(ITypeSymbol type, Compilation compilation) =>
        type.TypeKind != TypeKind.Interface && Contracts(type, compilation).Length != 0;

    internal static (int RetryCount, int TimeoutMilliseconds) Policy(ITypeSymbol type)
    {
        var attribute = type.GetAttributes().FirstOrDefault(item => item.AttributeClass?.ToDisplayString() == PolicyName);
        var retries = 0;
        var timeout = -1;
        if (attribute is not null)
            foreach (var argument in attribute.NamedArguments)
                if (argument.Key == "RetryCount") retries = (int)argument.Value.Value!;
                else if (argument.Key == "TimeoutMilliseconds") timeout = (int)argument.Value.Value!;
        return (retries, timeout);
    }

    internal static string? InvalidContract(INamedTypeSymbol type, Compilation compilation)
    {
        if (!type.IsRefLikeType || type.TypeKind != TypeKind.Struct) return "steps must be ref structs";
        var contracts = Contracts(type, compilation);
        if (contracts.Length != 1) return "implement exactly one synchronous or asynchronous step interface";
        var method = contracts[0].GetMembers().OfType<IMethodSymbol>().Single();
        if (type.FindImplementationForInterfaceMember(method) is not IMethodSymbol implementation ||
            implementation.DeclaredAccessibility != Accessibility.Public)
            return "the execution method must be public and directly callable";
        if (type.AllInterfaces.Any(contract => contract.ToDisplayString() == "System.IAsyncDisposable") ||
            (contracts[0].Name != "IStep" && type.GetMembers("Dispose").Length != 0) ||
            (contracts[0].Name != "IStep" && type.AllInterfaces.Any(contract => contract.ToDisplayString() == "System.IDisposable")))
            return "asynchronous cleanup must belong to the returned operation, not the step instance";
        var policy = Policy(type);
        if (policy.RetryCount < 0 || policy.TimeoutMilliseconds < -1 || policy.TimeoutMilliseconds == 0)
            return "RetryCount must be nonnegative; TimeoutMilliseconds must be -1 or positive";
        return null;
    }

    internal static bool IsService(IParameterSymbol parameter) => ServiceAttribute(parameter) is not null;

    internal static AttributeData? ServiceAttribute(IParameterSymbol parameter) => parameter.GetAttributes().FirstOrDefault(attribute =>
        attribute.AttributeClass?.ToDisplayString() == ServiceName);

    internal static bool SameType(ITypeSymbol left, ITypeSymbol right) =>
        SymbolEqualityComparer.IncludeNullability.Equals(left, right);

    internal static bool HasTypeParameter(ITypeSymbol type) => type.TypeKind == TypeKind.TypeParameter ||
        type is IArrayTypeSymbol array && HasTypeParameter(array.ElementType) ||
        type is INamedTypeSymbol named && (named.IsUnboundGenericType || named.TypeArguments.Any(HasTypeParameter) ||
            named.ContainingType is not null && HasTypeParameter(named.ContainingType));

    internal static string TypeName(ITypeSymbol type) => type.ToDisplayString(
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
}
