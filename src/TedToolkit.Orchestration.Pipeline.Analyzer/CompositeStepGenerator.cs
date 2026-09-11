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
    internal const string PrepareProtocolName = "__TedToolkitPrepareCompositeStep";
    internal const string ExecuteProtocolName = "__TedToolkitExecuteCompositeStep";
    internal const string StateProtocolName = "__TedToolkitCompositeStepState";
    private const string CompositeHintSuffix = ".CompositeStep.g.cs";

    internal static CompositeGenerationResult Generate(
        Compilation compilation,
        ImmutableArray<MethodDeclarationSyntax> methods,
        CSharpParseOptions options,
        CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var sources = ImmutableArray.CreateBuilder<GeneratedSource>();
        var symbols = methods.Select(method => (
                Syntax: method,
                Symbol: compilation.GetSemanticModel(method.SyntaxTree).GetDeclaredSymbol(method) as IMethodSymbol))
            .Where(item => item.Symbol is not null)
            .Select(item => (item.Syntax, Symbol: item.Symbol!))
            .ToArray();

        var configurations = new List<MethodDeclarationSyntax>();
        foreach (var item in symbols.Where(item => PipelineSymbols.IsCompositeCandidate(item.Symbol, compilation)))
        {
            var reason = ContractError(item.Symbol, compilation);
            if (reason is null) configurations.Add(item.Syntax);
            else diagnostics.Add(Diagnostic.Create(PipelineDiagnostics.StaticGraph,
                item.Syntax.GetLocation(), reason));
        }

        var leaves = symbols.Where(item => StepSymbols.IsLeafStep(item.Symbol)).ToArray();
        var validLeaves = new List<IMethodSymbol>();
        foreach (var item in leaves)
        {
            var reason = StepSymbols.InvalidContract(item.Symbol, compilation);
            if (reason is null) validLeaves.Add(item.Symbol);
        }

        foreach (var item in symbols.Where(item => StepSymbols.IsPipeline(item.Symbol) &&
            !StepSymbols.IsLeafStep(item.Symbol) &&
            !PipelineSymbols.IsCompositeCandidate(item.Symbol, compilation)))
            diagnostics.Add(Diagnostic.Create(PipelineDiagnostics.InvalidPipeline,
                item.Syntax.GetLocation(), item.Symbol.Name,
                "Pipeline can mark only a valid Step or Configuration method"));

        if (configurations.Count != 0)
            GenerateComposites(compilation, configurations, validLeaves, options,
                diagnostics, sources, cancellationToken);

        foreach (var leaf in validLeaves.Where(StepSymbols.IsPipeline))
        {
            var reason = PipelineFacadeEmitter.ContractError(leaf);
            if (reason is not null)
            {
                diagnostics.Add(Diagnostic.Create(PipelineDiagnostics.InvalidPipeline,
                    leaf.Locations.FirstOrDefault(location => location.IsInSource), leaf.Name, reason));
                continue;
            }
            _ = StepSymbols.TryGetReturnShape(leaf, out var result, out var synchronous);
            var factory = new StepFactory(leaf.ContainingType, leaf, result, synchronous, compilation);
            sources.Add(new GeneratedSource(
                GeneratedNames.HintName(leaf, ".Pipeline.g.cs"),
                PipelineFacadeEmitter.EmitLeaf(compilation, factory,
                    (MethodDeclarationSyntax)leaf.DeclaringSyntaxReferences.Single().GetSyntax())));
        }

        return new CompositeGenerationResult(sources.ToImmutable(), diagnostics.ToImmutable());
    }

    private static void GenerateComposites(Compilation compilation,
        IReadOnlyList<MethodDeclarationSyntax> configurations,
        IReadOnlyList<IMethodSymbol> leaves,
        CSharpParseOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ImmutableArray<GeneratedSource>.Builder sources,
        CancellationToken cancellationToken)
    {
        var prepared = PrepareCompilation(diagnostics.Add, compilation, configurations, leaves, options);
        if (prepared.Factories.Count == 0) return;
        sources.Add(new GeneratedSource("CompositeStepExtensions.g.cs", prepared.ExtensionsText));

        var graphs = new List<CompositeGraph>();
        foreach (var syntax in configurations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = (IMethodSymbol)prepared.Compilation.GetSemanticModel(syntax.SyntaxTree)
                .GetDeclaredSymbol(syntax)!;
            var factory = prepared.Factories.SingleOrDefault(candidate => candidate.IsComposite &&
                !candidate.IsExternal && SymbolEqualityComparer.Default.Equals(candidate.Type, method.ContainingType));
            if (factory is null) continue;
            var inputMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            for (var index = 0; index < factory.RuntimeInputs.Length; index++)
                inputMap.Add(factory.RuntimeInputs[index], "__root" + index);
            var names = new HashSet<string>(FactoryNames(syntax), StringComparer.Ordinal);
            var selected = prepared.Factories.Where(candidate => names.Contains(candidate.Name)).ToArray();
            var nodes = new GraphReader(diagnostics.Add, prepared.Compilation,
                syntax, selected, inputMap).Read();
            if (nodes is not null)
                graphs.Add(new CompositeGraph(syntax, method, factory, nodes));
        }

        if (!TryResolveGraphProperties(diagnostics.Add, graphs)) return;
        foreach (var graph in graphs)
        {
            var reason = StepSymbols.IsPipeline(graph.Configuration)
                ? PipelineFacadeEmitter.ContractError(graph.Configuration)
                : null;
            if (reason is not null)
            {
                diagnostics.Add(Diagnostic.Create(PipelineDiagnostics.InvalidPipeline,
                    graph.Configuration.Locations.FirstOrDefault(location => location.IsInSource),
                    graph.Configuration.Name, reason));
                continue;
            }
            sources.Add(new GeneratedSource(
                GeneratedNames.HintName(graph.Owner, CompositeHintSuffix),
                CompositeStepEmitter.Emit(prepared.Compilation, graph.Configuration,
                    graph.Syntax, graph.Nodes, graph.Factory.RuntimeInputs,
                    graph.Factory.RequiresServices, StepSymbols.IsPipeline(graph.Configuration))));
        }
    }

    private static (Compilation Compilation, List<StepFactory> Factories, string ExtensionsText)
        PrepareCompilation(Action<Diagnostic> reportDiagnostic, Compilation compilation,
            IReadOnlyList<MethodDeclarationSyntax> configurations,
            IReadOnlyList<IMethodSymbol> leaves, CSharpParseOptions options)
    {
        var stubs = configurations.Select(configuration => CompositeStub(compilation, configuration)).ToArray();
        var prepared = compilation.AddSyntaxTrees(stubs.Select((source, index) =>
            CSharpSyntaxTree.ParseText(source, options, "CompositeResults" + index + ".g.cs")));

        var factories = new List<StepFactory>();
        foreach (var leaf in leaves)
        {
            var rebound = prepared.GetTypeByMetadataName(GeneratedNames.MetadataName(leaf.ContainingType))!
                .GetMembers(leaf.Name).OfType<IMethodSymbol>().Single(candidate =>
                    candidate.Parameters.Length == leaf.Parameters.Length && StepSymbols.IsLeafStep(candidate));
            _ = StepSymbols.TryGetReturnShape(rebound, out var result, out var synchronous);
            factories.Add(new StepFactory(rebound.ContainingType, rebound, result, synchronous, prepared));
        }

        foreach (var syntax in configurations)
        {
            var method = (IMethodSymbol)prepared.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax)!;
            factories.Add(new StepFactory(method.ContainingType, method,
                method.ContainingType.GetTypeMembers("Results").Single(), true, prepared, composite: true));
        }

        var requestedNames = new HashSet<string>(configurations.SelectMany(FactoryNames), StringComparer.Ordinal);
        var externalFactories = CollectExternalCompositeFactories(
            reportDiagnostic, prepared, requestedNames).ToList();
        RejectUnaliasedMetadataCollisions(reportDiagnostic, externalFactories);
        factories.AddRange(externalFactories);
        var extensionsText = StepFactory.EmitExtensions(prepared, factories, PipelineSymbols.StepGraphName);
        var bound = prepared.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            extensionsText, options, "CompositeStepExtensions.g.cs"));
        return (bound, factories.Select(factory => factory.Rebind(bound)).ToList(), extensionsText);
    }

    internal static string? ContractError(IMethodSymbol method, Compilation compilation)
    {
        var owner = method.ContainingType;
        if (owner.TypeKind != TypeKind.Class || !owner.IsStatic || owner.IsFileLocal ||
            owner.ContainingType is not null || owner.Arity != 0 ||
            owner.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Public) ||
            !owner.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is ClassDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            return "Composite Step must be declared in a top-level, non-generic internal or public static partial class";
        if (method.MethodKind != MethodKind.Ordinary || !method.IsStatic || method.IsAsync ||
            method.IsGenericMethod || !method.ReturnsVoid || method.RefKind != RefKind.None ||
            method.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Public) ||
            method.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() is not MethodDeclarationSyntax { Body: not null })
            return "Configuration must be an internal or public static non-generic non-async void method with a body";
        if (StepSymbols.IsLeafStep(method))
            return "Configuration cannot also be marked Step";
        if (owner.GetMembers("Configuration").OfType<IMethodSymbol>().Count(candidate =>
            StepSymbols.IsUserAuthored(candidate)) != 1)
            return "Composite Step must declare exactly one Configuration method";
        var graph = compilation.GetTypeByMetadataName(PipelineSymbols.StepGraphName);
        if (method.Parameters.Length == 0 || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, graph) ||
            method.Parameters.Skip(1).Any(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, graph)))
            return "Configuration requires exactly one StepGraph as its first parameter";

        var tokens = method.Parameters.Where(StepSymbols.IsCancellationToken).ToArray();
        if (tokens.Length > 1 || tokens.Length == 1 &&
            (tokens[0].Ordinal != method.Parameters.Length - 1 || StepSymbols.IsService(tokens[0])))
            return "Configuration may declare only one unmarked trailing CancellationToken";
        if (method.Parameters.Skip(1).Any(parameter =>
            parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType ||
            parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
            StepSymbols.HasTypeParameter(parameter.Type)))
            return "Configuration parameters must be closed by-value types and cannot be ref-like, pointer, or function-pointer types";
        var logger = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        if (method.Parameters.Any(parameter => logger is not null &&
            SymbolEqualityComparer.Default.Equals(parameter.Type, logger) &&
            StepSymbols.ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value is string))
            return "a non-generic ILogger service cannot use a key";
        var reserved = new[] { "Results", StateProtocolName, PrepareProtocolName, ExecuteProtocolName };
        if (reserved.Any(name => owner.GetMembers(name).Any(StepSymbols.IsUserAuthored)))
            return "Composite Step declares a member reserved for generated execution";
        return null;
    }

    internal static bool IsProtocolMethod(IMethodSymbol method) =>
        ProtocolAttribute(method) is { ConstructorArguments.Length: 2 } attribute &&
        attribute.ConstructorArguments[0].Value is 1 &&
        attribute.ConstructorArguments[1].Value is bool;

    internal static bool HasValidProtocol(INamedTypeSymbol owner, Compilation compilation)
    {
        var protocols = owner.GetMembers().OfType<IMethodSymbol>()
            .Where(method => ProtocolAttribute(method) is not null).ToArray();
        return protocols.Length == 1 &&
            ProtocolMarkerError(protocols[0]) is null &&
            ProtocolError(owner, protocols[0], compilation) is null;
    }

    internal static ITypeSymbol ProtocolResult(IMethodSymbol method, out bool synchronous)
    {
        synchronous = true;
        if (method.ReturnType is INamedTypeSymbol named &&
            named.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>")
        {
            synchronous = false;
            return named.TypeArguments[0];
        }
        return method.ReturnType;
    }

    private static IEnumerable<StepFactory> CollectExternalCompositeFactories(
        Action<Diagnostic> reportDiagnostic, Compilation compilation, HashSet<string> requestedNames)
    {
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
            foreach (var owner in StepFactory.Types(reference.GlobalNamespace).Where(type =>
                type.DeclaredAccessibility == Accessibility.Public && requestedNames.Contains(type.Name)))
            {
                var protocols = owner.GetMembers().OfType<IMethodSymbol>()
                    .Where(method => ProtocolAttribute(method) is not null).ToArray();
                var hasProtocolMembers = owner.GetTypeMembers(StateProtocolName).Length != 0 ||
                    owner.GetMembers(PrepareProtocolName).Length != 0 ||
                    owner.GetMembers(ExecuteProtocolName).Length != 0;
                if (protocols.Length == 0)
                {
                    if (hasProtocolMembers)
                        reportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidProtocol,
                            owner.Locations.FirstOrDefault(), owner.ToDisplayString(),
                            "expected exactly one attributed execute method"));
                    continue;
                }
                var reason = protocols.Length == 1
                    ? ProtocolMarkerError(protocols[0]) ?? ProtocolError(owner, protocols[0], compilation)
                    : "expected exactly one attributed execute method";
                if (reason is not null)
                {
                    reportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidProtocol,
                        owner.Locations.FirstOrDefault(), owner.ToDisplayString(), reason));
                    continue;
                }
                var protocol = protocols[0];
                var attribute = ProtocolAttribute(protocol)!;
                var requiresServices = (bool)attribute.ConstructorArguments[1].Value!;
                var result = ProtocolResult(protocol, out var synchronous);
                yield return new StepFactory(owner, protocol, result, synchronous, compilation,
                    composite: true, external: true, requiresServices: requiresServices,
                    assemblyAlias: ReferenceAlias(compilation, owner.ContainingAssembly));
            }
    }

    private static string? ReferenceAlias(Compilation compilation, IAssemblySymbol assembly)
    {
        foreach (var reference in compilation.References)
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol candidate &&
                candidate.Identity.Equals(assembly.Identity))
                return reference.Properties.Aliases.FirstOrDefault(alias => alias != "global");
        return null;
    }

    private static void RejectUnaliasedMetadataCollisions(
        Action<Diagnostic> reportDiagnostic, List<StepFactory> factories)
    {
        var rejected = new HashSet<StepFactory>();
        foreach (var group in factories.GroupBy(factory => GeneratedNames.MetadataName(factory.Type),
            StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            var aliases = group.Select(factory => factory.AssemblyAlias).ToArray();
            if (aliases.All(alias => alias is not null) &&
                aliases.Distinct(StringComparer.Ordinal).Count() == aliases.Length)
                continue;
            foreach (var factory in group)
            {
                rejected.Add(factory);
                reportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidProtocol,
                    factory.Type.Locations.FirstOrDefault(), factory.Type.ToDisplayString(),
                    "referenced Composite types with the same metadata name require distinct extern aliases"));
            }
        }
        factories.RemoveAll(rejected.Contains);
    }

    private static AttributeData? ProtocolAttribute(IMethodSymbol method) =>
        method.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == StepSymbols.CompositeProtocolAttributeName);

    private static string? ProtocolMarkerError(IMethodSymbol execute)
    {
        if (execute.Name != ExecuteProtocolName)
            return "the protocol marker must be applied to the reserved execute method";
        var attribute = ProtocolAttribute(execute)!;
        if (attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not int version ||
            attribute.ConstructorArguments[1].Value is not bool)
            return "the protocol marker arguments are invalid";
        return version == 1 ? null : "protocol version " + version + " is unsupported; expected version 1";
    }

    private static string? ProtocolError(INamedTypeSymbol owner, IMethodSymbol execute, Compilation compilation)
    {
        if (owner.TypeKind != TypeKind.Class || !owner.IsStatic || owner.ContainingType is not null ||
            owner.Arity != 0 || owner.DeclaredAccessibility != Accessibility.Public)
            return "the protocol owner must be a top-level, non-generic public static class";
        if (execute.MethodKind != MethodKind.Ordinary || !execute.IsStatic || execute.IsAsync ||
            execute.IsGenericMethod || execute.RefKind != RefKind.None ||
            execute.DeclaredAccessibility != Accessibility.Public ||
            owner.GetMembers(ExecuteProtocolName).OfType<IMethodSymbol>().Count() != 1)
            return "execute must be the only public static non-generic method with its reserved name";

        var states = owner.GetTypeMembers(StateProtocolName);
        if (states.Length != 1 || states[0].TypeKind != TypeKind.Struct || !states[0].IsReadOnly ||
            states[0].Arity != 0 || states[0].DeclaredAccessibility != Accessibility.Public)
            return "state must be one public readonly non-generic nested struct";
        var state = states[0];

        var results = owner.GetTypeMembers("Results");
        if (results.Length != 1 || results[0].TypeKind != TypeKind.Struct || !results[0].IsReadOnly ||
            results[0].Arity != 0 || results[0].DeclaredAccessibility != Accessibility.Public)
            return "Results must be one public readonly non-generic nested struct";

        var prepares = owner.GetMembers(PrepareProtocolName).OfType<IMethodSymbol>().ToArray();
        if (prepares.Length != 1)
            return "expected exactly one prepare method";
        var prepare = prepares[0];
        if (prepare.MethodKind != MethodKind.Ordinary || !prepare.IsStatic || prepare.IsAsync ||
            prepare.IsGenericMethod || prepare.RefKind != RefKind.None ||
            prepare.DeclaredAccessibility != Accessibility.Public ||
            prepare.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
            return "prepare must be a public static non-generic method with by-value parameters";

        if (execute.Parameters.Length < 2 ||
            execute.Parameters.Any(parameter => parameter.RefKind != RefKind.None ||
                parameter.Type.IsRefLikeType ||
                parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
                StepSymbols.HasTypeParameter(parameter.Type)) ||
            !SymbolEqualityComparer.Default.Equals(execute.Parameters[0].Type, state) ||
            !StepSymbols.IsCancellationToken(execute.Parameters[execute.Parameters.Length - 1]) ||
            !execute.Parameters[execute.Parameters.Length - 1].HasExplicitDefaultValue ||
            execute.Parameters.Skip(1).Take(execute.Parameters.Length - 2).Any(StepSymbols.IsCancellationToken))
            return "execute parameters must be state, closed by-value data, and one defaulted trailing CancellationToken";

        var attribute = ProtocolAttribute(execute)!;
        var requiresServices = (bool)attribute.ConstructorArguments[1].Value!;
        var expectedPrepareCount = requiresServices ? 2 : 1;
        if (prepare.Parameters.Length != expectedPrepareCount ||
            !SymbolEqualityComparer.Default.Equals(prepare.ReturnType, state) ||
            prepare.Parameters[prepare.Parameters.Length - 1].Type.SpecialType != SpecialType.System_String ||
            requiresServices && !SymbolEqualityComparer.Default.Equals(prepare.Parameters[0].Type,
                compilation.GetTypeByMetadataName("System.IServiceProvider")))
            return "prepare signature does not match the protocol marker";
        var result = ProtocolResult(execute, out _);
        return SymbolEqualityComparer.Default.Equals(result, results[0])
            ? null
            : "execute must return the generated Results type or Task<Results>";
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
                if (byType.TryGetValue(node.Factory.Type, out var nested) && !Visit(nested)) return false;
            graph.Factory.IsSynchronous = graph.Nodes.All(node => node.Factory.IsSynchronous);
            graph.Factory.RequiresServices = graph.Configuration.Parameters.Any(StepSymbols.IsService) ||
                graph.Nodes.Any(node => node.Factory.RequiresServices);
            visiting.Remove(graph.Owner);
            visited.Add(graph.Owner);
            return true;
        }
        return graphs.All(Visit);
    }

    private static string CompositeStub(Compilation compilation, MethodDeclarationSyntax syntax)
    {
        var method = (IMethodSymbol)compilation.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax)!;
        var owner = method.ContainingType;
        var declaration = new TypeDeclaration(owner.Name, TypeDeclarationType.CLASS).Static.Partial;
        _ = owner.DeclaredAccessibility == Accessibility.Public ? declaration.Public : declaration.Internal;
        var results = new TypeDeclaration("Results", TypeDeclarationType.STRUCT).Readonly;
        _ = owner.DeclaredAccessibility == Accessibility.Public && method.DeclaredAccessibility == Accessibility.Public
            ? results.Public : results.Internal;
        declaration.AddMember(results);
        return Render(GeneratedNames.Namespace(owner.ContainingNamespace), declaration);
    }

    private static IEnumerable<string> FactoryNames(MethodDeclarationSyntax configuration) =>
        configuration.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => (call.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? "");

    private sealed class CompositeGraph
    {
        internal CompositeGraph(MethodDeclarationSyntax syntax, IMethodSymbol configuration,
            StepFactory factory, IReadOnlyList<GraphNode> nodes)
        {
            Syntax = syntax;
            Configuration = configuration;
            Owner = configuration.ContainingType;
            Factory = factory;
            Nodes = nodes;
        }
        internal MethodDeclarationSyntax Syntax { get; }
        internal IMethodSymbol Configuration { get; }
        internal INamedTypeSymbol Owner { get; }
        internal StepFactory Factory { get; }
        internal IReadOnlyList<GraphNode> Nodes { get; }
    }
}
