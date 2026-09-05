using System.Linq;
using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Symbol identity is shared by generation and usage diagnostics; unrelated names do not opt in.
internal static class PipelineSymbols
{
    internal const string PipelineName = "TedToolkit.Orchestration.Pipeline.Pipeline";
    internal const string BuilderName = PipelineName + "+Builder";

    internal static bool InheritsPipeline(INamedTypeSymbol? owner, Compilation compilation)
    {
        var pipeline = compilation.GetTypeByMetadataName(PipelineName);
        for (var current = owner?.BaseType; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, pipeline)) return true;
        return false;
    }

    internal static bool IsConfiguration(IMethodSymbol method, Compilation compilation) =>
        (method.Name is "Configure" or "Configuration") &&
        SymbolEqualityComparer.Default.Equals(method.Parameters.FirstOrDefault()?.Type,
            compilation.GetTypeByMetadataName(BuilderName)) &&
        (InheritsPipeline(method.ContainingType, compilation) ||
            SymbolEqualityComparer.Default.Equals(method.ContainingType, compilation.GetTypeByMetadataName(PipelineName)));

    internal static bool IsStepFactory(IMethodSymbol method, Compilation compilation)
    {
        method = method.ReducedFrom ?? method;
        if (!method.IsExtensionMethod ||
            !SymbolEqualityComparer.Default.Equals(method.Parameters.FirstOrDefault()?.Type,
                compilation.GetTypeByMetadataName(BuilderName))) return false;
        return method.ContainingType.Name == method.Name + "Extensions" &&
            method.ContainingNamespace.ToDisplayString() == "TedToolkit.Orchestration.Pipeline" &&
            (SymbolEqualityComparer.Default.Equals(method.ReturnType.OriginalDefinition, compilation.GetTypeByMetadataName(StepSymbols.BuilderName)) ||
             SymbolEqualityComparer.Default.Equals(method.ReturnType, compilation.GetTypeByMetadataName(StepSymbols.VoidBuilderName)));
    }
}
