using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

/// <summary>Checks step contracts and reports declarations or policies used outside generated execution.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PipelineAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(PipelineDiagnostics.StaticGraph, PipelineDiagnostics.StepMustBeInternal,
            PipelineDiagnostics.InvalidContract,
            PipelineDiagnostics.FactoryOutsideConfiguration, PipelineDiagnostics.ConfigurationInvocation,
            PipelineDiagnostics.ModifierOutsideConfiguration);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(StepSymbols.StepName) is null) return;
            start.RegisterSymbolAction(AnalyzeStep, SymbolKind.NamedType);
            start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static void AnalyzeStep(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (StepContextEmitter.HasAttribute(type, StepContextEmitter.CompositeAttributeName))
        {
            var compositeReason = CompositeStepGenerator.ContractError(type, context.Compilation);
            if (compositeReason is not null)
                context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.StaticGraph,
                    type.Locations[0], compositeReason));
            return;
        }
        if (!StepSymbols.IsStep(type, context.Compilation)) return;
        if (type.DeclaredAccessibility != Accessibility.Internal || type.IsFileLocal)
            context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.StepMustBeInternal, type.Locations[0], type.Name));
        var reason = StepSymbols.InvalidContract(type, context.Compilation);
        if (reason is not null)
            context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidContract, type.Locations[0], type.Name, reason));
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (PipelineSymbols.IsCompositeConfiguration(method, context.Compilation))
        {
            context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.ConfigurationInvocation,
                invocation.Syntax.GetLocation(), method.Name));
            return;
        }
        if (PipelineSymbols.IsStepFactory(method, context.Compilation))
        {
            if (context.ContainingSymbol is not IMethodSymbol caller ||
                !PipelineSymbols.IsCompositeConfiguration(caller, context.Compilation))
                context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.FactoryOutsideConfiguration,
                    invocation.Syntax.GetLocation(), method.Name));
            return;
        }
        if (PipelineSymbols.IsStepModifier(method, context.Compilation))
        {
            if (context.ContainingSymbol is not IMethodSymbol caller ||
                !PipelineSymbols.IsCompositeConfiguration(caller, context.Compilation))
                context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.ModifierOutsideConfiguration,
                    invocation.Syntax.GetLocation(), method.Name));
            return;
        }
    }
}
