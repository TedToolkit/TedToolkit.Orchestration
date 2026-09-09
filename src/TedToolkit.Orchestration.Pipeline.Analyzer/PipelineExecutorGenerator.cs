using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

/// <summary>Generates Step context and Composite Step execution APIs.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class PipelineExecutorGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var stepContextCandidates = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is StructDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword),
            static (syntax, cancellationToken) =>
                StepContextEmitter.TryCreate(syntax, cancellationToken))
            .Where(static source => source is not null)
            .Select(static (source, _) => source!)
            .WithTrackingName("StepContextCandidates");
        var stepContextSources = stepContextCandidates.Collect()
            .Select(static (sources, _) => StepContextEmitter.Resolve(sources))
            .WithTrackingName("StepContextSources");
        context.RegisterSourceOutput(stepContextSources, static (output, sources) =>
        {
            foreach (var source in sources)
                output.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8));
        });

        var configurations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is MethodDeclarationSyntax
                { Identifier.ValueText: "Configuration" },
            static (syntax, _) => (MethodDeclarationSyntax)syntax.Node).Collect();

        var comparedCompositeResults = context.CompilationProvider
            .Combine(configurations)
            .Combine(context.ParseOptionsProvider)
            .Combine(stepContextSources)
            .Select(static (input, cancellationToken) => CompositeStepGenerator.Generate(
                input.Left.Left.Left,
                input.Left.Left.Right,
                (CSharpParseOptions)input.Left.Right,
                input.Right,
                cancellationToken))
            .WithComparer(EqualityComparer<CompositeGenerationResult>.Default);
        var compositeSources = comparedCompositeResults
            .Select(static (result, _) => result)
            .WithTrackingName("CompositeSources");
        context.RegisterSourceOutput(compositeSources, static (output, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                output.ReportDiagnostic(diagnostic);
            foreach (var source in result.Sources)
                output.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8));
        });
    }
}
