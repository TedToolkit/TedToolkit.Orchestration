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
        ImmutableArray.Create(PipelineDiagnostics.StaticGraph, PipelineDiagnostics.InvalidContract,
            PipelineDiagnostics.FactoryOutsideConfiguration, PipelineDiagnostics.ConfigurationInvocation,
            PipelineDiagnostics.ModifierOutsideConfiguration, PipelineDiagnostics.InvalidPipeline,
            PipelineDiagnostics.InvalidProtocol);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(StepSymbols.StepAttributeName) is null) return;
            start.RegisterSymbolAction(AnalyzeStep, SymbolKind.Method);
            start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        });
    }

    private static void AnalyzeStep(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (StepSymbols.IsLeafStep(method))
        {
            var reason = StepSymbols.InvalidContract(method, context.Compilation);
            if (reason is not null)
                context.ReportDiagnostic(Diagnostic.Create(
                    PipelineDiagnostics.InvalidContract, method.Locations[0], method.Name, reason));
        }
        if (PipelineSymbols.IsCompositeCandidate(method, context.Compilation))
        {
            var reason = CompositeStepGenerator.ContractError(method, context.Compilation);
            if (reason is not null)
                context.ReportDiagnostic(Diagnostic.Create(
                    PipelineDiagnostics.StaticGraph, method.Locations[0], reason));
        }
        if (!StepSymbols.IsPipeline(method)) return;
        if (!StepSymbols.IsLeafStep(method) &&
            !PipelineSymbols.IsCompositeCandidate(method, context.Compilation))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PipelineDiagnostics.InvalidPipeline, method.Locations[0], method.Name,
                "Pipeline can mark only a valid Step or Configuration method"));
            return;
        }
        var pipelineReason = PipelineFacadeEmitter.ContractError(method);
        if (pipelineReason is not null)
            context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidPipeline,
                method.Locations[0], method.Name, pipelineReason));
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
