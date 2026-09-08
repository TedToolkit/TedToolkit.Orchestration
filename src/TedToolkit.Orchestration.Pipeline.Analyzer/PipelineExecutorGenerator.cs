using System.Collections.Immutable;
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
        context.RegisterSourceOutput(context.CompilationProvider, static (output, compilation) =>
        {
            foreach (var contextSource in StepContextEmitter.Emit(compilation))
                output.AddSource(contextSource.HintName, SourceText.From(contextSource.Source, Encoding.UTF8));
        });

        var configurations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is MethodDeclarationSyntax
                { Identifier.ValueText: "Configuration" },
            static (syntax, _) => (MethodDeclarationSyntax)syntax.Node).Collect();

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(configurations).Combine(context.ParseOptionsProvider),
            static (output, input) => Generate(
                output,
                input.Left.Left,
                input.Left.Right,
                (CSharpParseOptions)input.Right));
    }

    private static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<MethodDeclarationSyntax> methods,
        CSharpParseOptions options) =>
        CompositeStepGenerator.Generate(context, compilation, methods, options);
}
