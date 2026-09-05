using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.StateMachine.Analyzer;

internal static class StateMachineDiagnostics
{
    internal static readonly DiagnosticDescriptor InvalidDeclaration = new(
        "TTSM001",
        "State machine declaration cannot be generated",
        "Cannot generate this state machine: {0}",
        "StateMachine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DirectStateAssignment = new(
        "TTSM002",
        "State machine state cannot be assigned directly",
        "State on state machine '{0}' can only be changed by generated triggers",
        "StateMachine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}