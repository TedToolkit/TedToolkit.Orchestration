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
    private const string COMPOSITE_HINT_SUFFIX = ".CompositeStep.g.cs";

    internal static string ResultTypeName(IMethodSymbol declaration) =>
        declaration.Name + "Result";

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
                "Pipeline can mark only a valid Step or Composite declaration method"));

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
                !candidate.IsExternal && SymbolEqualityComparer.Default.Equals(candidate.Callable, method));
            if (factory is null) continue;
            var inputMap = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            for (var index = 0; index < factory.RuntimeInputs.Length; index++)
                inputMap.Add(factory.RuntimeInputs[index], "__root" + index);
            var names = new HashSet<string>(FactoryRequests(prepared.Compilation, syntax)
                .Select(request => request.Name), StringComparer.Ordinal);
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
                GeneratedNames.HintName(graph.Configuration, COMPOSITE_HINT_SUFFIX),
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
                method.ContainingType.GetTypeMembers(ResultTypeName(method)).Single(),
                true, prepared, composite: true));
        }

        var requests = configurations.SelectMany(configuration =>
            FactoryRequests(prepared, configuration)).ToArray();
        var externalFactories = CollectExternalCompositeFactories(
            reportDiagnostic, prepared, requests).ToList();
        RejectUnaliasedMetadataCollisions(reportDiagnostic, externalFactories);
        factories.AddRange(externalFactories);
        var extensionsText = StepFactory.EmitExtensions(prepared, factories, PipelineSymbols.STEP_GRAPH_NAME);
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
            return "Composite declaration must be an internal or public static non-generic non-async void method with a body";
        if (StepSymbols.IsLeafStep(method))
            return "Composite declaration cannot also be marked Step";
        var parameterError = DeclarationParameterError(method, compilation);
        if (parameterError is not null) return parameterError;
        if (owner.GetMembers(ResultTypeName(method)).Any(StepSymbols.IsUserAuthored) ||
            owner.GetMembers(method.Name).OfType<IMethodSymbol>().Any(candidate =>
                !SymbolEqualityComparer.Default.Equals(candidate, method) &&
                StepSymbols.IsUserAuthored(candidate)))
            return "Composite Step declares a member reserved for generated execution";
        return null;
    }

    internal static bool HasValidProtocol(IMethodSymbol declaration, Compilation compilation)
    {
        var owner = declaration.ContainingType;
        var declarations = owner.GetMembers(declaration.Name).OfType<IMethodSymbol>()
            .Where(method => PipelineSymbols.IsCompositeCandidate(method, compilation)).ToArray();
        return declarations.Length == 1 &&
            FindProtocol(owner, declaration, compilation, out _, out _, out _) is not null;
    }

    internal static IMethodSymbol? FindProtocol(
        INamedTypeSymbol owner,
        IMethodSymbol declaration,
        Compilation compilation,
        out ITypeSymbol? result,
        out bool synchronous,
        out string? reason)
    {
        result = null;
        synchronous = true;
        reason = ProtocolOwnerError(owner, declaration);
        if (reason is not null) return null;
        reason = DeclarationParameterError(declaration, compilation);
        if (reason is not null) return null;

        var results = owner.GetTypeMembers(ResultTypeName(declaration));
        if (results.Length != 1 || results[0].TypeKind != TypeKind.Struct ||
            !results[0].IsReadOnly || results[0].Arity != 0 ||
            results[0].DeclaredAccessibility != Accessibility.Public)
        {
            reason = ResultTypeName(declaration) +
                " must be one public readonly non-generic nested struct";
            return null;
        }

        var candidates = owner.GetMembers(declaration.Name).OfType<IMethodSymbol>()
            .Where(method => !PipelineSymbols.IsCompositeCandidate(method, compilation))
            .Where(method => ProtocolMethodError(
                declaration, method, results[0], compilation) is null)
            .ToArray();
        if (candidates.Length != 1)
        {
            reason = candidates.Length == 0
                ? "expected one structurally matching same-name execute overload"
                : "multiple structurally matching same-name execute overloads were found";
            return null;
        }

        var execute = candidates[0];
        result = results[0];
        synchronous = !(execute.ReturnType is INamedTypeSymbol named &&
            named.OriginalDefinition.ToDisplayString() ==
                "System.Threading.Tasks.Task<TResult>");
        return execute;
    }

    private static IEnumerable<StepFactory> CollectExternalCompositeFactories(
        Action<Diagnostic> reportDiagnostic, Compilation compilation,
        IReadOnlyList<FactoryRequest> requests)
    {
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
            foreach (var owner in StepFactory.Types(reference.GlobalNamespace).Where(type =>
                type.DeclaredAccessibility == Accessibility.Public))
            {
                var declarationGroups = owner.GetMembers().OfType<IMethodSymbol>()
                    .Where(method => requests.Any(request => request.Name == method.Name) &&
                        PipelineSymbols.IsCompositeCandidate(method, compilation))
                    .GroupBy(method => method.Name, StringComparer.Ordinal);
                foreach (var declarations in declarationGroups)
                {
                    var candidates = declarations.ToArray();
                    var matching = requests.Where(request => request.Name == declarations.Key &&
                        (request.Carrier is null || candidates.Any(candidate =>
                            MatchesCarrier(request.Carrier, candidate)))).ToArray();
                    if (matching.Length == 0) continue;
                    var identity = owner.ToDisplayString() + "." + declarations.Key;
                    if (candidates.Length != 1)
                    {
                        reportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidProtocol,
                            owner.Locations.FirstOrDefault(), identity,
                            "expected exactly one public Composite declaration method with this name"));
                        continue;
                    }
                    var protocol = FindProtocol(owner, candidates[0], compilation,
                        out var result, out var synchronous, out var reason);
                    if (protocol is null)
                    {
                        reportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidProtocol,
                            owner.Locations.FirstOrDefault(), identity, reason));
                        continue;
                    }
                    var requiresServices = ProtocolRequiresServices(protocol, compilation);
                    var factory = new StepFactory(owner, candidates[0], result!, synchronous, compilation,
                        composite: true, external: true, requiresServices: requiresServices,
                        assemblyAliases: ReferenceAliases(compilation, owner.ContainingAssembly));
                    factory.UseExtensionTypeNames(matching.Select(request => request.Carrier)
                        .OfType<string>().Where(carrier =>
                            MatchesCarrier(carrier, candidates[0])));
                    yield return factory;
                }
            }
    }

    private static IReadOnlyList<string> ReferenceAliases(
        Compilation compilation, IAssemblySymbol assembly)
    {
        foreach (var reference in compilation.References)
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol candidate &&
                candidate.Identity.Equals(assembly.Identity))
                return reference.Properties.Aliases.Where(alias => alias != "global")
                    .Distinct(StringComparer.Ordinal).OrderBy(alias => alias, StringComparer.Ordinal)
                    .ToArray();
        return Array.Empty<string>();
    }

    private static void RejectUnaliasedMetadataCollisions(
        Action<Diagnostic> reportDiagnostic, List<StepFactory> factories)
    {
        var rejected = new HashSet<StepFactory>();
        foreach (var group in factories.GroupBy(factory =>
            GeneratedNames.MetadataName(factory.Type) + "|" + factory.Callable.Name,
            StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            if (TryAssignDistinctAliases(group.ToArray())) continue;
            foreach (var factory in group)
            {
                rejected.Add(factory);
                reportDiagnostic(Diagnostic.Create(PipelineDiagnostics.InvalidProtocol,
                    factory.Type.Locations.FirstOrDefault(), factory.Identity,
                    "referenced Composite functions with the same metadata owner and name require distinct extern aliases"));
            }
        }
        factories.RemoveAll(rejected.Contains);
    }

    private static bool TryAssignDistinctAliases(IReadOnlyList<StepFactory> factories)
    {
        var ordered = factories.OrderBy(factory => factory.AssemblyAliases.Count)
            .ThenBy(factory => factory.Type.ContainingAssembly.Identity.Name, StringComparer.Ordinal)
            .ToArray();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var selected = new Dictionary<StepFactory, string>();
        if (!Assign(0)) return false;
        foreach (var item in selected) item.Key.UseAssemblyAlias(item.Value);
        return true;

        bool Assign(int index)
        {
            if (index == ordered.Length) return true;
            foreach (var alias in ordered[index].AssemblyAliases)
            {
                if (!used.Add(alias)) continue;
                selected.Add(ordered[index], alias);
                if (Assign(index + 1)) return true;
                selected.Remove(ordered[index]);
                used.Remove(alias);
            }
            return false;
        }
    }

    private static string? ProtocolOwnerError(
        INamedTypeSymbol owner, IMethodSymbol declaration)
    {
        if (owner.TypeKind != TypeKind.Class || !owner.IsStatic || owner.ContainingType is not null ||
            owner.Arity != 0 || owner.DeclaredAccessibility != Accessibility.Public)
            return "the protocol owner must be a top-level, non-generic public static class";
        if (declaration.MethodKind != MethodKind.Ordinary || !declaration.IsStatic || declaration.IsAsync ||
            declaration.IsGenericMethod || !declaration.ReturnsVoid || declaration.RefKind != RefKind.None ||
            declaration.DeclaredAccessibility != Accessibility.Public)
            return "the Composite declaration must be a public static non-generic non-async void method";
        return null;
    }

    private static string? DeclarationParameterError(
        IMethodSymbol declaration, Compilation compilation)
    {
        var graph = compilation.GetTypeByMetadataName(PipelineSymbols.STEP_GRAPH_NAME);
        if (declaration.Parameters.Length == 0 ||
            !SymbolEqualityComparer.Default.Equals(declaration.Parameters[0].Type, graph) ||
            declaration.Parameters.Skip(1).Any(parameter =>
                SymbolEqualityComparer.Default.Equals(parameter.Type, graph)))
            return "Composite declaration requires exactly one StepGraph as its first parameter";

        var tokens = declaration.Parameters.Where(StepSymbols.IsCancellationToken).ToArray();
        if (tokens.Length > 1 || tokens.Length == 1 &&
            (tokens[0].Ordinal != declaration.Parameters.Length - 1 || StepSymbols.IsService(tokens[0])))
            return "Composite declaration may declare only one unmarked trailing CancellationToken";
        if (declaration.Parameters.Skip(1).Any(parameter =>
            parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType ||
            parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
            StepSymbols.HasTypeParameter(parameter.Type)))
            return "Composite declaration parameters must be closed by-value types and cannot be ref-like, pointer, or function-pointer types";

        var logger = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        if (declaration.Parameters.Any(parameter => logger is not null &&
            SymbolEqualityComparer.Default.Equals(parameter.Type, logger) &&
            StepSymbols.ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value is string))
            return "a non-generic ILogger service cannot use a key";
        return null;
    }

    private static string? ProtocolMethodError(
        IMethodSymbol declaration,
        IMethodSymbol execute,
        INamedTypeSymbol resultType,
        Compilation compilation)
    {
        if (execute.MethodKind != MethodKind.Ordinary || !execute.IsStatic ||
            execute.IsGenericMethod || execute.RefKind != RefKind.None ||
            execute.DeclaredAccessibility != Accessibility.Public)
            return "execute must be a public static non-generic method";

        var returned = execute.ReturnType;
        if (returned is INamedTypeSymbol task &&
            task.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>")
            returned = task.TypeArguments[0];
        if (!SymbolEqualityComparer.Default.Equals(returned, resultType))
            return "execute must return the generated named result type or Task of that type";

        var runtimeInputs = declaration.Parameters.Skip(1)
            .Where(parameter => !StepSymbols.IsCancellationToken(parameter)).ToArray();
        var requiresServices = ProtocolRequiresServices(execute, compilation);
        if (runtimeInputs.Any(StepSymbols.IsService) && !requiresServices)
            return "execute must begin with IServiceProvider when the declaration has service inputs";
        var expectedCount = (requiresServices ? 1 : 0) + 1 + runtimeInputs.Length + 1;
        if (execute.Parameters.Length != expectedCount || execute.Parameters.Any(parameter =>
                parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType ||
                parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
                StepSymbols.HasTypeParameter(parameter.Type)))
            return "execute parameters do not match the structural Composite protocol";

        var offset = 0;
        if (requiresServices)
        {
            if (!SymbolEqualityComparer.Default.Equals(execute.Parameters[0].Type,
                    compilation.GetTypeByMetadataName("System.IServiceProvider")))
                return "execute must begin with IServiceProvider when services are required";
            offset++;
        }
        if (execute.Parameters[offset].Type.SpecialType != SpecialType.System_String)
            return "execute display path must precede Composite inputs";
        offset++;
        for (var index = 0; index < runtimeInputs.Length; index++)
        {
            var source = runtimeInputs[index];
            var projected = execute.Parameters[offset + index];
            if (!SymbolEqualityComparer.IncludeNullability.Equals(projected.Type, source.Type) ||
                projected.HasExplicitDefaultValue != source.HasExplicitDefaultValue ||
                projected.HasExplicitDefaultValue && !Equals(projected.ExplicitDefaultValue, source.ExplicitDefaultValue) ||
                !ServiceContractEquals(projected, source))
                return "execute Composite inputs must preserve declaration order, type, default, and service role";
        }
        var token = execute.Parameters[execute.Parameters.Length - 1];
        if (!StepSymbols.IsCancellationToken(token) || !token.HasExplicitDefaultValue ||
            StepSymbols.IsService(token))
            return "execute must end with one unmarked defaulted CancellationToken";
        return null;
    }

    internal static bool ProtocolRequiresServices(
        IMethodSymbol execute, Compilation compilation) =>
        execute.Parameters.Length != 0 &&
        SymbolEqualityComparer.Default.Equals(execute.Parameters[0].Type,
            compilation.GetTypeByMetadataName("System.IServiceProvider"));

    private static bool ServiceContractEquals(IParameterSymbol left, IParameterSymbol right)
    {
        var leftAttribute = StepSymbols.ServiceAttribute(left);
        var rightAttribute = StepSymbols.ServiceAttribute(right);
        if ((leftAttribute is null) != (rightAttribute is null)) return false;
        return leftAttribute is null || Equals(
            leftAttribute.ConstructorArguments.FirstOrDefault().Value,
            rightAttribute!.ConstructorArguments.FirstOrDefault().Value);
    }

    private static bool TryResolveGraphProperties(
        Action<Diagnostic> reportDiagnostic, IReadOnlyList<CompositeGraph> graphs)
    {
        var byMethod = graphs.ToDictionary(
            graph => graph.Configuration, SymbolEqualityComparer.Default);
        var visiting = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        bool Visit(CompositeGraph graph)
        {
            if (visited.Contains(graph.Configuration)) return true;
            if (!visiting.Add(graph.Configuration))
            {
                reportDiagnostic(Diagnostic.Create(PipelineDiagnostics.StaticGraph,
                    graph.Syntax.GetLocation(), "Composite Step dependency cycle detected"));
                return false;
            }
            foreach (var node in graph.Nodes.Where(node => node.Factory.IsComposite))
                if (byMethod.TryGetValue(node.Factory.Callable, out var nested) && !Visit(nested)) return false;
            graph.Factory.IsSynchronous = graph.Nodes.All(node => node.Factory.IsSynchronous);
            graph.Factory.RequiresServices = graph.Configuration.Parameters.Any(StepSymbols.IsService) ||
                graph.Nodes.Any(node => node.Factory.RequiresServices);
            visiting.Remove(graph.Configuration);
            visited.Add(graph.Configuration);
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
        var results = new TypeDeclaration(ResultTypeName(method), TypeDeclarationType.STRUCT).Readonly;
        _ = owner.DeclaredAccessibility == Accessibility.Public && method.DeclaredAccessibility == Accessibility.Public
            ? results.Public : results.Internal;
        declaration.AddMember(results);
        return Render(GeneratedNames.Namespace(owner.ContainingNamespace), declaration);
    }

    private static IEnumerable<FactoryRequest> FactoryRequests(
        Compilation compilation, MethodDeclarationSyntax configuration)
    {
        var model = compilation.GetSemanticModel(configuration.SyntaxTree);
        var graph = model.GetDeclaredSymbol(configuration.ParameterList.Parameters[0]);
        foreach (var call in configuration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax member) continue;
            var name = member.Name.Identifier.ValueText;
            var direct = SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(member.Expression).Symbol, graph);
            var firstArgument = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            var carrier = RightmostIdentifier(member.Expression);
            var explicitCarrier = firstArgument is not null &&
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(firstArgument).Symbol, graph) &&
                carrier is not null && carrier.EndsWith(
                    name.Replace("_", "__") + "Extensions", StringComparison.Ordinal);
            if (direct) yield return new FactoryRequest(name, null);
            else if (explicitCarrier) yield return new FactoryRequest(name, carrier);
        }
    }

    private static string? RightmostIdentifier(ExpressionSyntax expression) => expression switch
    {
        SimpleNameSyntax name => name.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => expression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>()
            .LastOrDefault()?.Identifier.ValueText
    };

    private static bool MatchesCarrier(string carrier, IMethodSymbol declaration)
    {
        var basic = GeneratedNames.ExtensionTypeName(declaration);
        var namespaced = GeneratedNames.ExtensionTypeName(declaration, includeNamespace: true);
        var assembly = GeneratedNames.ExtensionTypeName(
            declaration, includeNamespace: true, includeAssembly: true);
        return carrier == basic || carrier == namespaced || carrier == assembly ||
            carrier == GeneratedNames.DisambiguateIdentifier(assembly, declaration);
    }

    private sealed class FactoryRequest
    {
        internal FactoryRequest(string name, string? carrier)
        {
            Name = name;
            Carrier = carrier;
        }

        internal string Name { get; }
        internal string? Carrier { get; }
    }

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
