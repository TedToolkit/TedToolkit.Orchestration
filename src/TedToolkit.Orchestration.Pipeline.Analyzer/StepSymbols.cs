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

    internal static bool IsLeafStep(ITypeSymbol type, Compilation compilation) =>
        type.TypeKind != TypeKind.Interface && Contracts(type, compilation).Length != 0;

    internal static string? InvalidContract(INamedTypeSymbol type, Compilation compilation)
    {
        if (!type.IsRefLikeType || type.TypeKind != TypeKind.Struct) return "steps must be ref structs";
        if (!type.IsReadOnly || type.DeclaredAccessibility != Accessibility.Internal ||
            type.IsFileLocal || type.ContainingType is not null || type.Arity != 0 ||
            !type.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax declaration &&
                declaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
            return "steps must be top-level, non-generic internal readonly ref partial structs";
        if (StepContextEmitter.HasExplicitLayout(type))
            return "steps cannot use explicit struct layout because generated context adds fields";
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
        if (type.GetMembers().Any(member =>
            member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true } &&
            member.Locations.Any(location => location.SourceTree?.FilePath.EndsWith(
                ".StepContext.g.cs", System.StringComparison.Ordinal) != true)))
            return "user-authored required members are not supported";
        var constructors = UsableConstructors(type, compilation);
        if (constructors.Length != 1)
            return "declare exactly one constructor accessible to generated execution";
        if (constructors[0].Parameters.Any(parameter =>
            parameter.RefKind != RefKind.None ||
            parameter.Type.IsRefLikeType ||
            parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer))
            return "constructor parameters must be by-value and cannot be ref-like, pointer, or function-pointer types";
        return null;
    }

    internal static IMethodSymbol[] UsableConstructors(INamedTypeSymbol type, Compilation compilation)
    {
        var explicitConstructors = type.InstanceConstructors.Where(constructor =>
            !constructor.IsImplicitlyDeclared &&
            compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly)).ToArray();
        return explicitConstructors.Length != 0
            ? explicitConstructors
            : type.InstanceConstructors.Where(constructor =>
                constructor.IsImplicitlyDeclared &&
                constructor.Parameters.Length == 0 &&
                compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly)).ToArray();
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
