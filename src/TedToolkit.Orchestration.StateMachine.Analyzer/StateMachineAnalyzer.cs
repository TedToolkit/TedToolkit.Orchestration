using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TedToolkit.Orchestration.StateMachine.Analyzer;

/// <summary>Protects generated state-machine ownership rules in consumer source.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StateMachineAnalyzer : DiagnosticAnalyzer
{
    private const string MachineAttributeName =
        "TedToolkit.Orchestration.StateMachine.StateMachineAttribute`1";
    private const string MachineBaseName =
        "TedToolkit.Orchestration.StateMachine.StateMachine`1";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(StateMachineDiagnostics.DirectStateAssignment);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var machineAttribute = start.Compilation.GetTypeByMetadataName(MachineAttributeName);
            var machineBase = start.Compilation.GetTypeByMetadataName(MachineBaseName);
            if (machineAttribute is null || machineBase is null) return;
            start.RegisterOperationAction(
                operation => AnalyzeAssignment(operation, machineAttribute, machineBase),
                OperationKind.SimpleAssignment);
        });
    }

    private static void AnalyzeAssignment(
        OperationAnalysisContext context,
        INamedTypeSymbol machineAttribute,
        INamedTypeSymbol machineBase)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is not IPropertyReferenceOperation { Property.Name: "State" } target ||
            !SymbolEqualityComparer.Default.Equals(target.Property.ContainingType.OriginalDefinition, machineBase) ||
            context.ContainingSymbol.ContainingType is not { } containingType ||
            !containingType.GetAttributes().Any(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass?.OriginalDefinition, machineAttribute)))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            StateMachineDiagnostics.DirectStateAssignment,
            target.Syntax.GetLocation(),
            containingType.Name));
    }
}