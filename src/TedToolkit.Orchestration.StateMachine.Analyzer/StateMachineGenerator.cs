using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TedToolkit.Orchestration.StateMachine.Analyzer;

/// <summary>Generates state storage inheritance and direct implementations for partial trigger methods.</summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class StateMachineGenerator : IIncrementalGenerator
{
    private const string MachineAttributeName = "TedToolkit.Orchestration.StateMachine.StateMachineAttribute`1";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var machines = context.SyntaxProvider.ForAttributeWithMetadataName(
            MachineAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (syntax, _) => (INamedTypeSymbol)syntax.TargetSymbol);
        context.RegisterSourceOutput(machines, static (output, machine) => Generate(output, machine));
    }

    private static void Generate(SourceProductionContext context, INamedTypeSymbol machine)
    {
        var location = machine.Locations.FirstOrDefault(item => item.IsInSource) ?? Location.None;
        if (!TryCreateModel(machine, out var model, out var error))
        {
            context.ReportDiagnostic(Diagnostic.Create(StateMachineDiagnostics.InvalidDeclaration, location, error));
            return;
        }

        context.AddSource(HintName(machine), SourceText.From(Emit(model!), Encoding.UTF8));
    }
}