using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

/// <summary>Suppresses CS0282 only for valid generated-context Pipeline Steps.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GeneratedStepContextSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor Descriptor = new(
        "TTPSPR001",
        "CS0282",
        "Generated Pipeline Step context does not depend on partial struct field order.");

    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [Descriptor];

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != "CS0282" || !diagnostic.Location.IsInSource) continue;
            var tree = diagnostic.Location.SourceTree;
            if (tree is null) continue;
            var root = tree.GetRoot(context.CancellationToken);
            var declaration = root.FindNode(diagnostic.Location.SourceSpan)
                .AncestorsAndSelf().OfType<StructDeclarationSyntax>().FirstOrDefault();
            if (declaration is null) continue;
            var symbol = FindSymbol(declaration, context.Compilation);
            if (symbol is null || !StepContextEmitter.CanGenerate(symbol, context.Compilation)) continue;
            context.ReportSuppression(Suppression.Create(Descriptor, diagnostic));
        }
    }

    private static INamedTypeSymbol? FindSymbol(StructDeclarationSyntax declaration, Compilation compilation)
    {
        var namespaces = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse().Select(item => item.Name.ToString());
        var name = string.Join(".", namespaces.Append(declaration.Identifier.ValueText));
        return compilation.GetTypeByMetadataName(name);
    }

}
