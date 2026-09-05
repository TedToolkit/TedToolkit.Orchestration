using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class PipelineDiagnostics
{
    internal static readonly DiagnosticDescriptor ResultTypeMismatch = Create("TTP001", "Step result type mismatch",
        "Step result '{0}' must exactly match parameter '{1}' of type '{2}', including nullability");
    internal static readonly DiagnosticDescriptor InvalidStep = Create("TTP004", "Step cannot be constructed",
        "Step '{0}' must be an accessible, closed, non-abstract, top-level ref struct with one usable constructor and no required members");
    internal static readonly DiagnosticDescriptor InvalidParameter = Create("TTP008", "Unsupported constructor parameter",
        "Step parameter '{0}' must be passed by value and cannot be a pointer, function pointer, or ref-like type");
    internal static readonly DiagnosticDescriptor StaticGraph = Create("TTP009", "Pipeline graph must be statically known",
        "Cannot generate this pipeline: {0}. Use direct generated factories and local variables that are never reassigned");
    internal static readonly DiagnosticDescriptor StepMustBeInternal = Create("TTP012", "Step types must be internal",
        "Step '{0}' must have internal accessibility");

    internal static readonly DiagnosticDescriptor InvalidContract = Create("TTP013", "Invalid step contract",
        "Step '{0}': {1}");

    internal static readonly DiagnosticDescriptor FactoryOutsideConfiguration = Create("TTP014", "Step factory used outside configuration",
        "Factory '{0}' only declares a step inside Configuration; this call does not execute or register runtime work", DiagnosticSeverity.Warning);
    internal static readonly DiagnosticDescriptor ConfigurationInvocation = Create("TTP015", "Configuration is not an execution method",
        "'{0}' declares the generated graph; call a generated Execute method to run the pipeline", DiagnosticSeverity.Warning);
    internal static readonly DiagnosticDescriptor PolicyBypassed = Create("TTP016", "Direct step call bypasses its policy",
        "Direct execution of '{0}' bypasses its StepPolicy; use a generated pipeline when retry or timeout is required", DiagnosticSeverity.Info);

    private static DiagnosticDescriptor Create(string id, string title, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        new(id, title, message, "Pipeline", severity, isEnabledByDefault: true);
}
