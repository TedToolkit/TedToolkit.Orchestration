using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

/// <summary>Generates named executors and their typed configuration and execution APIs.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class PipelineExecutorGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configurations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is MethodDeclarationSyntax { Identifier.ValueText: "Configure" or "Configuration" },
            static (syntax, _) => (MethodDeclarationSyntax)syntax.Node).Collect();
        context.RegisterSourceOutput(context.CompilationProvider.Combine(configurations).Combine(context.ParseOptionsProvider),
            static (output, input) => Generate(output, input.Left.Left, input.Left.Right, (CSharpParseOptions)input.Right));
    }

    private static void Generate(SourceProductionContext context, Compilation compilation,
        ImmutableArray<MethodDeclarationSyntax> methods, CSharpParseOptions options)
    {
        var step = compilation.GetTypeByMetadataName(StepSymbols.StepName);
        if (step is null || compilation.GetTypeByMetadataName(PipelineSymbols.PipelineName) is null) return;

        var configurations = methods.Where(method =>
            compilation.GetSemanticModel(method.SyntaxTree).GetDeclaredSymbol(method) is IMethodSymbol symbol &&
            PipelineSymbols.InheritsPipeline(symbol.ContainingType, compilation) &&
            PipelineSymbols.IsConfiguration(symbol, compilation)).ToArray();
        var requestedNames = new HashSet<string>(configurations.SelectMany(FactoryNames), StringComparer.Ordinal);
        var factories = CollectFactories(context, compilation, step, requestedNames);
        var extensions = SourceText.From(StepFactory.EmitExtensions(compilation, factories), Encoding.UTF8);
        context.AddSource("PipelineStepExtensions.g.cs", extensions);

        // Bind generated extensions once for the entire compilation, not once per pipeline.
        var augmented = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(extensions, options, "PipelineStepExtensions.g.cs"));
        var boundFactories = factories.Select(factory => factory.Rebind(augmented)).ToArray();
        foreach (var configuration in configurations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            GenerateExecutor(context, augmented, configuration, boundFactories);
        }
    }

    private static void GenerateExecutor(SourceProductionContext context, Compilation compilation,
        MethodDeclarationSyntax configuration, IReadOnlyList<StepFactory> factories)
    {
        var method = (IMethodSymbol)compilation.GetSemanticModel(configuration.SyntaxTree).GetDeclaredSymbol(configuration)!;
        var error = ConfigurationError(configuration, method, compilation);
        if (error is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.StaticGraph, configuration.GetLocation(), error));
            return;
        }

        var names = new HashSet<string>(FactoryNames(configuration), StringComparer.Ordinal);
        var selected = factories.Where(factory => names.Contains(factory.Type.Name)).ToArray();
        var nodes = new GraphReader(context, compilation, configuration, selected).Read();
        if (nodes is null) return;
        context.AddSource(method.ContainingType.ToDisplayString() + ".Pipeline.g.cs",
            ExecutorEmitter.Emit(compilation, method.ContainingType, configuration, nodes));
    }

    private static IEnumerable<string> FactoryNames(MethodDeclarationSyntax configuration) =>
        configuration.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => (call.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? "");

    private static string? ConfigurationError(MethodDeclarationSyntax syntax, IMethodSymbol method, Compilation compilation)
    {
        var owner = method.ContainingType;
        if (owner.TypeKind != TypeKind.Class || owner.IsStatic || owner.IsAbstract || owner.IsFileLocal ||
            owner.ContainingType is not null || owner.Arity != 0 ||
            syntax.Parent is not ClassDeclarationSyntax declaration || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return "use a concrete, non-generic, top-level partial pipeline class";
        if (syntax.Body is null || method.IsAsync || method.IsGenericMethod || !method.ReturnsVoid ||
            syntax.ParameterList.Parameters.Any(parameter => parameter.Modifiers.Count != 0))
            return "configuration requires a non-async void body and by-value parameters";
        if (owner.GetMembers().OfType<IMethodSymbol>().Count(candidate => PipelineSymbols.IsConfiguration(candidate, compilation)) != 1)
            return "declare exactly one Configure or Configuration method";
        if (owner.InstanceConstructors.Any(constructor => !constructor.IsImplicitlyDeclared))
            return "remove explicit constructors; the generator supplies the pipeline constructor";
        if (ReservedMembers.Any(name => owner.GetMembers(name).Length != 0))
            return "rename members that collide with Builder, Results, Execute methods, or _services";
        if (!owner.IsSealed && !method.IsSealed && (method.IsVirtual || method.IsOverride))
            return "seal the pipeline class or its configuration override so derived classes cannot replace the generated graph";
        if (method.Name == "Configuration" &&
            !SymbolEqualityComparer.Default.Equals(method.OverriddenMethod?.ContainingType.OriginalDefinition,
                compilation.GetTypeByMetadataName(PipelineSymbols.PipelineName)))
            return "Configuration must override Pipeline.Configuration using Pipeline.Builder";
        return null;
    }

    private static readonly string[] ReservedMembers =
        { "Builder", "Results", "Execute", "ExecuteWithoutResults", "ExecuteAsync", "ExecuteWithoutResultsAsync", "_services" };

    private static List<StepFactory> CollectFactories(SourceProductionContext context, Compilation compilation,
        INamedTypeSymbol step, HashSet<string> requestedNames)
    {
        var types = StepFactory.Types(compilation.Assembly.GlobalNamespace).ToList();
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            // Only assemblies depending on the Pipeline runtime can supply external step types.
            if (reference.Modules.Any(module => module.ReferencedAssemblySymbols.Any(assembly =>
                assembly.Identity.Name == step.ContainingAssembly.Identity.Name)))
                types.AddRange(StepFactory.Types(reference.GlobalNamespace).Where(type => requestedNames.Contains(type.Name)));
        }
        var factories = new List<StepFactory>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {

            var result = StepSymbols.ResultType(type, compilation);
            if (!StepSymbols.IsStep(type, compilation)) continue;
            var reason = StepSymbols.InvalidContract(type, compilation);
            if (reason is not null)
            {
                if (requestedNames.Contains(type.Name)) context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidContract,
                    type.Locations.FirstOrDefault(location => location.IsInSource), type.Name, reason));
                continue;
            }
            if (type.DeclaredAccessibility != Accessibility.Internal && !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
            {
                if (requestedNames.Contains(type.Name)) context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.StepMustBeInternal,
                    Location.None, type.Name));
                continue;
            }
            var constructors = type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared && compilation.IsSymbolAccessibleWithin(c, compilation.Assembly)).ToArray();
            if (constructors.Length == 0) constructors = type.InstanceConstructors.Where(c => c.IsImplicitlyDeclared && c.Parameters.Length == 0).ToArray();
            if (type.IsFileLocal || type.ContainingType is not null || StepSymbols.HasTypeParameter(type) || constructors.Length != 1 ||
                !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) || HasRequiredMembers(type))
            {
                if (requestedNames.Contains(type.Name)) context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidStep,
                    type.Locations.FirstOrDefault(l => l.IsInSource), type.Name));
                continue;
            }
            var invalid = constructors[0].Parameters.FirstOrDefault(p => p.RefKind != RefKind.None ||
                p.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer || p.Type.IsRefLikeType);
            if (invalid is not null)
            {
                if (requestedNames.Contains(type.Name)) context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidParameter,
                    invalid.Locations.FirstOrDefault(l => l.IsInSource), invalid.Name));
                continue;
            }
            var factory = new StepFactory(type, constructors[0], result, StepSymbols.IsSynchronous(type, compilation));
            if (names.Add(factory.Type.Name)) factories.Add(factory);
            else context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.StaticGraph, type.Locations.FirstOrDefault(location => location.IsInSource), "step factory names must be unique"));
        }
        return factories;
    }

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })) return true;
        return false;
    }
}

