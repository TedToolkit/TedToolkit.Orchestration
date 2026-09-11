using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

/// <summary>Generates function-declared Step factories, Composite execution, and Pipeline facades.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class PipelineExecutorGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is MethodDeclarationSyntax method &&
                (method.ParameterList.Parameters.Count != 0 || method.AttributeLists.Count != 0),
            static (syntax, _) => (MethodDeclarationSyntax)syntax.Node).Collect();

        var comparedResults = context.CompilationProvider
            .Combine(methods)
            .Combine(context.ParseOptionsProvider)
            .Select(static (input, cancellationToken) => CompositeStepGenerator.Generate(
                input.Left.Left,
                input.Left.Right,
                (CSharpParseOptions)input.Right,
                cancellationToken))
            .WithComparer(EqualityComparer<CompositeGenerationResult>.Default);

        context.RegisterSourceOutput(comparedResults, static (output, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                output.ReportDiagnostic(diagnostic);
            foreach (var source in result.Sources)
                output.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8));
        });
    }
}
