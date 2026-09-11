using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Symbol identity is shared by generation and usage diagnostics; unrelated names do not opt in.
internal static class PipelineSymbols
{
    internal const string StepGraphName = "TedToolkit.Orchestration.Pipeline.StepGraph";

    internal static bool IsCompositeCandidate(IMethodSymbol method, Compilation compilation) =>
        method.Name == "Configuration" &&
        SymbolEqualityComparer.Default.Equals(method.Parameters.FirstOrDefault()?.Type,
            compilation.GetTypeByMetadataName(StepGraphName));

    internal static bool IsCompositeConfiguration(IMethodSymbol method, Compilation compilation)
    {
        if (!IsCompositeCandidate(method, compilation)) return false;
        return method.DeclaringSyntaxReferences.Length == 0
            ? CompositeStepGenerator.HasValidProtocol(method.ContainingType, compilation)
            : CompositeStepGenerator.ContractError(method, compilation) is null;
    }

    internal static bool IsStepFactory(IMethodSymbol method, Compilation compilation)
    {
        method = method.ReducedFrom ?? method;
        if (!method.IsExtensionMethod) return false;
        var receiver = method.Parameters.FirstOrDefault()?.Type;
        if (!SymbolEqualityComparer.Default.Equals(receiver, compilation.GetTypeByMetadataName(StepGraphName)))
            return false;
        var generatedOwner = method.ContainingType.Name == method.Name + "Extensions" ||
            method.ContainingType.Name.EndsWith("_" + method.Name + "Extensions", StringComparison.Ordinal);
        return generatedOwner &&
            method.ContainingNamespace.ToDisplayString() == "TedToolkit.Orchestration.Pipeline" &&
            (SymbolEqualityComparer.Default.Equals(method.ReturnType.OriginalDefinition, compilation.GetTypeByMetadataName(StepSymbols.BuilderName)) ||
             SymbolEqualityComparer.Default.Equals(method.ReturnType, compilation.GetTypeByMetadataName(StepSymbols.VoidBuilderName)));
    }

    internal static bool IsStepModifier(IMethodSymbol method, Compilation compilation)
    {
        if (method.Name is not ("DependsOn" or "WithRetry" or "WithTimeout" or "WithDisplayName")) return false;
        var owner = method.ContainingType.OriginalDefinition;
        return SymbolEqualityComparer.Default.Equals(owner, compilation.GetTypeByMetadataName(StepSymbols.BuilderName)) ||
            SymbolEqualityComparer.Default.Equals(owner, compilation.GetTypeByMetadataName(StepSymbols.VoidBuilderName));
    }
}
