using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class PipelineDiagnostics
{
    internal static readonly DiagnosticDescriptor ResultTypeMismatch = Create("TTP001", "Step result type mismatch",
        "Step result '{0}' must exactly match parameter '{1}' of type '{2}', including nullability");
    internal static readonly DiagnosticDescriptor StaticGraph = Create("TTP009", "Pipeline graph must be statically known",
        "Cannot generate this pipeline: {0}. Use direct generated factories and local variables that are never reassigned");
    internal static readonly DiagnosticDescriptor InvalidContract = Create("TTP013", "Invalid step contract",
        "Step '{0}': {1}");

    internal static readonly DiagnosticDescriptor FactoryOutsideConfiguration = Create("TTP014", "Step factory used outside Composite declaration",
        "Factory '{0}' only declares a step inside a Composite declaration; this call does not execute or register runtime work", DiagnosticSeverity.Warning);
    internal static readonly DiagnosticDescriptor ConfigurationInvocation = Create("TTP015", "Composite declaration is not an execution method",
        "'{0}' declares the generated graph; call a generated Pipeline Execute method to run it");
    internal static readonly DiagnosticDescriptor ModifierOutsideConfiguration = Create("TTP017", "Node modifier used outside Composite declaration",
        "Modifier '{0}' only declares node behavior inside a Composite declaration; this call has no runtime effect", DiagnosticSeverity.Warning);
    internal static readonly DiagnosticDescriptor InvalidPipeline = Create("TTP018", "Invalid Pipeline entry",
        "Pipeline entry '{0}': {1}");
    internal static readonly DiagnosticDescriptor InvalidProtocol = Create("TTP019", "Invalid Composite Step protocol",
        "Composite Step '{0}': {1}");
    private static DiagnosticDescriptor Create(string id, string title, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        new(id, title, message, "Pipeline", severity, isEnabledByDefault: true);
}
