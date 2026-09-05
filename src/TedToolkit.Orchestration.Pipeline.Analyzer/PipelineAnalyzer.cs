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
        ImmutableArray.Create(PipelineDiagnostics.StepMustBeInternal, PipelineDiagnostics.InvalidContract,
            PipelineDiagnostics.FactoryOutsideConfiguration, PipelineDiagnostics.ConfigurationInvocation,
            PipelineDiagnostics.PolicyBypassed);

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
        if (PipelineSymbols.IsConfiguration(method, context.Compilation))
        {
            context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.ConfigurationInvocation,
                invocation.Syntax.GetLocation(), method.Name));
            return;
        }
        if (PipelineSymbols.IsStepFactory(method, context.Compilation))
        {
            if (context.ContainingSymbol is not IMethodSymbol caller ||
                !PipelineSymbols.IsConfiguration(caller, context.Compilation))
                context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.FactoryOutsideConfiguration,
                    invocation.Syntax.GetLocation(), method.Name));
            return;
        }
        if (method.Name is not ("Execute" or "ExecuteAsync") ||
            !StepSymbols.IsStep(method.ContainingType, context.Compilation)) return;
        var policy = StepSymbols.Policy(method.ContainingType);
        if (policy.RetryCount == 0 && policy.TimeoutMilliseconds == -1) return;
        var contract = StepSymbols.Contracts(method.ContainingType, context.Compilation);
        if (contract.Length != 1) return;
        foreach (var member in contract[0].GetMembers())
            if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method))
                context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.PolicyBypassed,
                    invocation.Syntax.GetLocation(), method.ContainingType.Name));
    }
}
