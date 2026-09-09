using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;
using Accessibility = Microsoft.CodeAnalysis.Accessibility;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class CompositeStepGenerator
{
    private const string CompositeHintSuffix = ".CompositeStep.g.cs";

    internal static CompositeGenerationResult Generate(
        Compilation compilation,
        ImmutableArray<MethodDeclarationSyntax> methods,
        CSharpParseOptions options,
        ImmutableArray<GeneratedSource> contextSources,
        CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var sources = ImmutableArray.CreateBuilder<GeneratedSource>();
        var configurations = methods.Where(method =>
        {
            var symbol = compilation.GetSemanticModel(method.SyntaxTree).GetDeclaredSymbol(method);
            return symbol is IMethodSymbol candidate &&
                StepContextEmitter.HasAttribute(candidate.ContainingType, StepContextEmitter.CompositeAttributeName);
        }).ToArray();
        if (configurations.Length == 0)
            return new CompositeGenerationResult(sources.ToImmutable(), diagnostics.ToImmutable());

        var valid = new List<MethodDeclarationSyntax>();
        foreach (var configuration in configurations)
        {
            var method = (IMethodSymbol)compilation.GetSemanticModel(configuration.SyntaxTree)
                .GetDeclaredSymbol(configuration)!;
            var reason = ContractError(configuration, method, compilation);
            if (reason is null) valid.Add(configuration);
            else diagnostics.Add(Diagnostic.Create(
                PipelineDiagnostics.StaticGraph, configuration.GetLocation(), reason));
        }
        if (valid.Count == 0)
            return new CompositeGenerationResult(sources.ToImmutable(), diagnostics.ToImmutable());

        var prepared = PrepareCompilation(
            diagnostics.Add, compilation, valid, options, contextSources);
        var bound = prepared.Compilation;
        var factories = prepared.Factories;
        sources.Add(new GeneratedSource("CompositeStepExtensions.g.cs", prepared.ExtensionsText));

        var graphs = new List<CompositeGraph>();
        foreach (var syntax in valid)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = (IMethodSymbol)bound.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax)!;
            var factory = factories.Single(candidate => candidate.IsComposite &&
                SymbolEqualityComparer.Default.Equals(candidate.Type, method.ContainingType));
            var inputs = factory.Constructor.Parameters;
            var inputMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            for (var index = 0; index < inputs.Length; index++)
                inputMap.Add(inputs[index], "__root" + index);
            var names = new HashSet<string>(FactoryNames(syntax), StringComparer.Ordinal);
            var selected = factories.Where(candidate => names.Contains(candidate.Type.Name)).ToArray();
            var nodes = new GraphReader(diagnostics.Add, bound, syntax, selected, inputMap).Read();
            if (nodes is not null) graphs.Add(new CompositeGraph(syntax, method.ContainingType, factory, inputs, nodes));
        }

        if (!TryResolveGraphProperties(diagnostics.Add, graphs))
            return new CompositeGenerationResult(sources.ToImmutable(), diagnostics.ToImmutable());
        var graphSources = graphs.Select(graph => (
            Graph: graph,
            MetadataName: GeneratedNames.MetadataName(graph.Owner),
            HintName: GeneratedNames.HintName(graph.Owner, CompositeHintSuffix))).ToArray();
        var hintCollisions = new HashSet<string>(graphSources
            .GroupBy(source => source.HintName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key), StringComparer.Ordinal);
        foreach (var source in graphSources)
        {
            var hintName = hintCollisions.Contains(source.HintName)
                ? GeneratedNames.DisambiguateHintName(
                    source.HintName, source.MetadataName, CompositeHintSuffix)
                : source.HintName;
            sources.Add(new GeneratedSource(hintName,
                CompositeStepEmitter.Emit(bound, source.Graph.Owner, source.Graph.Syntax,
                    source.Graph.Nodes, source.Graph.Inputs, source.Graph.Factory.RequiresServices)));
        }
        return new CompositeGenerationResult(sources.ToImmutable(), diagnostics.ToImmutable());
    }

    private static (Compilation Compilation, List<StepFactory> Factories, string ExtensionsText)
        PrepareCompilation(
            Action<Diagnostic> reportDiagnostic,
            Compilation compilation,
            IReadOnlyList<MethodDeclarationSyntax> configurations,
            CSharpParseOptions options,
            ImmutableArray<GeneratedSource> contextSources)
    {
        // Context members must be present before Composite result stubs are bound.
        var prepared = compilation.AddSyntaxTrees(contextSources.Select(source =>
            CSharpSyntaxTree.ParseText(source.Source, options, source.HintName)));
        // Results types must exist before generated Step factories can be typed.
        var stubs = configurations.Select(configuration => CompositeStub(prepared, configuration)).ToArray();
        prepared = prepared.AddSyntaxTrees(stubs.Select((source, index) =>
            CSharpSyntaxTree.ParseText(source, options, "CompositeResults" + index + ".g.cs")));

        var requestedNames = new HashSet<string>(
            configurations.SelectMany(FactoryNames), StringComparer.Ordinal);
        var factories = CollectLeafFactories(reportDiagnostic, prepared, requestedNames);
        factories.AddRange(CollectCompositeFactories(prepared, configurations, requestedNames));
        var extensionsText = StepFactory.EmitExtensions(
            prepared, factories, PipelineSymbols.StepGraphName);
        // Configuration invocations bind only after their generated extension methods exist.
        var bound = prepared.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            extensionsText, options, "CompositeStepExtensions.g.cs"));
        return (bound, factories.Select(factory => factory.Rebind(bound)).ToList(), extensionsText);
    }

    internal static string? ContractError(INamedTypeSymbol owner, Compilation compilation)
    {
        var methods = owner.GetMembers("Configuration").OfType<IMethodSymbol>()
            .Where(candidate => candidate.Locations.Any(location => location.IsInSource)).ToArray();
        if (methods.Length != 1)
            return "Composite Step must declare exactly one Configuration method";
        var syntax = methods[0].DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<MethodDeclarationSyntax>().SingleOrDefault();
        return syntax is null
            ? "Composite Step requires exactly private void Configuration(StepGraph steps) with a body"
            : ContractError(syntax, methods[0], compilation);
    }

    private static string? ContractError(MethodDeclarationSyntax syntax, IMethodSymbol method,
        Compilation compilation)
    {
        var owner = method.ContainingType;
        if (owner.TypeKind != TypeKind.Struct || !owner.IsRefLikeType || !owner.IsReadOnly ||
            owner.IsFileLocal || owner.ContainingType is not null || owner.Arity != 0 ||
            owner.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Public) ||
            syntax.Parent is not StructDeclarationSyntax declaration ||
            !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return "Composite Step must be a top-level public or internal readonly ref partial struct";
        if (StepContextEmitter.HasExplicitLayout(owner))
            return "Composite Step cannot use explicit struct layout because generated context adds fields";
        if (method.Name != "Configuration" || method.DeclaredAccessibility != Accessibility.Private ||
            method.IsStatic || method.IsAsync || method.IsGenericMethod || !method.ReturnsVoid ||
            method.Parameters.Length != 1 || method.RefKind != RefKind.None || syntax.Body is null ||
            !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type,
                compilation.GetTypeByMetadataName(PipelineSymbols.StepGraphName)))
            return "Composite Step requires exactly private void Configuration(StepGraph steps) with a body";
        if (owner.GetMembers().OfType<IMethodSymbol>().Count(candidate =>
            candidate.Name == "Configuration") != 1)
            return "Composite Step must declare exactly one Configuration method";
        if (StepSymbols.IsLeafStep(owner, compilation))
            return "Composite Step cannot also implement a leaf Step interface";
        if (new[] { "Results", "Pipeline", "Execute", "ExecuteAsync", "ExecuteWithoutResults", "ExecuteWithoutResultsAsync" }
            .Any(name => owner.GetMembers(name).Any(IsUserAuthored)))
            return "Composite Step declares a member reserved for generated execution";
        if (owner.GetMembers().OfType<IPropertySymbol>().Any(property =>
            property.IsRequired && IsUserAuthored(property)))
            return "Composite Step cannot declare user-authored required members";
        var constructor = PrimaryConstructor(owner);
        if (constructor is null) return "Composite Step must have zero inputs or one primary-constructor input list";
        foreach (var parameter in constructor.Parameters)
        {
            if (parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType ||
                parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
                StepSymbols.IsService(parameter))
                return "Composite Step inputs must be by-value data; inject services into leaf Steps";
        }
        return null;
    }

    private static bool IsUserAuthored(ISymbol symbol) => symbol.Locations.Any(location =>
        location.IsInSource &&
        location.SourceTree?.FilePath.EndsWith(".StepContext.g.cs", StringComparison.Ordinal) != true &&
        location.SourceTree?.FilePath.EndsWith(".CompositeStep.g.cs", StringComparison.Ordinal) != true);

    private static IMethodSymbol? PrimaryConstructor(INamedTypeSymbol type)
    {
        var declarations = type.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .OfType<StructDeclarationSyntax>().Where(declaration => declaration.ParameterList is not null)
            .ToArray();
        if (declarations.Length == 0)
            return type.InstanceConstructors.All(constructor => constructor.IsImplicitlyDeclared)
                ? type.InstanceConstructors.SingleOrDefault(constructor => constructor.Parameters.Length == 0)
                : null;
        if (declarations.Length != 1) return null;
        var declaration = declarations[0];
        return type.InstanceConstructors.SingleOrDefault(constructor =>
            constructor.DeclaringSyntaxReferences.Any(reference =>
                reference.SyntaxTree == declaration.SyntaxTree && reference.Span == declaration.Span));
    }

    private static List<StepFactory> CollectLeafFactories(Action<Diagnostic> reportDiagnostic,
        Compilation compilation, HashSet<string> requestedNames)
    {
        var factories = new List<StepFactory>();
        foreach (var type in StepFactory.Types(compilation.Assembly.GlobalNamespace))
        {
            if (!StepSymbols.IsLeafStep(type, compilation)) continue;
            var reason = StepSymbols.InvalidContract(type, compilation);
            if (reason is not null)
            {
                if (requestedNames.Contains(type.Name)) reportDiagnostic(Diagnostic.Create(
                    PipelineDiagnostics.InvalidContract, type.Locations.FirstOrDefault(location => location.IsInSource),
                    type.Name, reason));
                continue;
            }
            var constructor = StepSymbols.UsableConstructors(type, compilation).Single();
            factories.Add(new StepFactory(type, constructor, StepSymbols.ResultType(type, compilation),
                StepSymbols.IsSynchronous(type, compilation), compilation));
        }
        return factories;
    }

    private static IEnumerable<StepFactory> CollectCompositeFactories(Compilation compilation,
        IReadOnlyList<MethodDeclarationSyntax> configurations, HashSet<string> requestedNames)
    {
        foreach (var syntax in configurations)
        {
            var method = (IMethodSymbol)compilation.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax)!;
            var owner = method.ContainingType;
            var constructor = PrimaryConstructor(owner)!;
            yield return new StepFactory(owner, constructor, owner.GetTypeMembers("Results").Single(),
                true, compilation, true);
        }
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
            foreach (var owner in StepFactory.Types(reference.GlobalNamespace).Where(type =>
                type.DeclaredAccessibility == Accessibility.Public &&
                requestedNames.Contains(type.Name) &&
                StepContextEmitter.HasAttribute(type, StepContextEmitter.CompositeAttributeName)))
            {
                var resultContract = owner.AllInterfaces.FirstOrDefault(contract =>
                    contract.OriginalDefinition.ToDisplayString() is
                        "TedToolkit.Orchestration.Pipeline.ICompositeStep<TResult>" or
                        "TedToolkit.Orchestration.Pipeline.IAsyncCompositeStep<TResult>");
                var constructor = owner.InstanceConstructors.Where(candidate =>
                    compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly))
                    .OrderByDescending(candidate => candidate.Parameters.Length).FirstOrDefault();
                if (resultContract is null || constructor is null) continue;
                var synchronous = resultContract.Name == "ICompositeStep";
                var factory = new StepFactory(owner, constructor, resultContract.TypeArguments[0],
                    synchronous, compilation, true);
                factory.RequiresServices = owner.GetTypeMembers("Pipeline").SelectMany(type =>
                    type.InstanceConstructors).Any(candidate => candidate.Parameters.Any(parameter =>
                        parameter.Type.ToDisplayString() == "System.IServiceProvider"));
                yield return factory;
            }
    }

    private static bool TryResolveGraphProperties(
        Action<Diagnostic> reportDiagnostic, IReadOnlyList<CompositeGraph> graphs)
    {
        var byType = graphs.ToDictionary(graph => graph.Owner, SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        bool Visit(CompositeGraph graph)
        {
            if (visited.Contains(graph.Owner)) return true;
            if (!visiting.Add(graph.Owner))
            {
                reportDiagnostic(Diagnostic.Create(PipelineDiagnostics.StaticGraph,
                    graph.Syntax.GetLocation(), "Composite Step dependency cycle detected"));
                return false;
            }
            foreach (var node in graph.Nodes.Where(node => node.Factory.IsComposite))
                if (byType.TryGetValue(node.Factory.Type, out var nested) && !Visit(nested))
                    return false;
            graph.Factory.IsSynchronous = graph.Nodes.All(node => node.Factory.IsSynchronous);
            graph.Factory.RequiresServices = graph.Factory.HasLogger ||
                graph.Nodes.Any(node => node.Factory.RequiresServices);
            visiting.Remove(graph.Owner);
            visited.Add(graph.Owner);
            return true;
        }
        foreach (var graph in graphs)
            if (!Visit(graph)) return false;
        return true;
    }

    private static string CompositeStub(Compilation compilation, MethodDeclarationSyntax syntax)
    {
        var owner = ((IMethodSymbol)compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetDeclaredSymbol(syntax)!).ContainingType;
        var declaration = new TypeDeclaration(owner.Name, TypeDeclarationType.REF_STRUCT)
            .Readonly.Partial;
        _ = owner.DeclaredAccessibility == Accessibility.Public
            ? declaration.Public
            : declaration.Internal;
        declaration.AddMember(new TypeDeclaration("Results", TypeDeclarationType.STRUCT)
            .Public.Readonly);
        return Render(GeneratedNames.Namespace(owner.ContainingNamespace), declaration);
    }

    private static IEnumerable<string> FactoryNames(MethodDeclarationSyntax configuration) =>
        configuration.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => (call.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? "");

    private sealed class CompositeGraph
    {
        internal CompositeGraph(MethodDeclarationSyntax syntax, INamedTypeSymbol owner,
            StepFactory factory, IReadOnlyList<IParameterSymbol> inputs, IReadOnlyList<GraphNode> nodes)
        {
            Syntax = syntax;
            Owner = owner;
            Factory = factory;
            Inputs = inputs;
            Nodes = nodes;
        }

        internal MethodDeclarationSyntax Syntax { get; }
        internal INamedTypeSymbol Owner { get; }
        internal StepFactory Factory { get; }
        internal IReadOnlyList<IParameterSymbol> Inputs { get; }
        internal IReadOnlyList<GraphNode> Nodes { get; }
    }
}
