using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Owns the generated typed StepGraph factory surface for one callable Step.
internal sealed class StepFactory
{
    private readonly List<string> _additionalExtensionTypeNames = new();

    internal StepFactory(INamedTypeSymbol type, IMethodSymbol callable, ITypeSymbol? result,
        bool synchronous, Compilation compilation, bool composite = false,
        bool external = false, bool requiresServices = false,
        IReadOnlyList<string>? assemblyAliases = null)
    {
        Type = type;
        Callable = callable;
        Result = result;
        IsSynchronous = synchronous;
        IsComposite = composite;
        IsExternal = external;
        AssemblyAliases = assemblyAliases ?? Array.Empty<string>();
        AssemblyAlias = AssemblyAliases.FirstOrDefault();
        SpecialLoggerParameters = callable.Parameters
            .Where(parameter => IsNonGenericLogger(parameter, compilation) &&
                StepSymbols.ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value is null)
            .ToArray();
        HasLogger = SpecialLoggerParameters.Length != 0;
        RequiresServices = requiresServices || HasLogger || callable.Parameters.Any(StepSymbols.IsService);
        Parameters = composite
            ? callable.Parameters.Skip(1).Where(parameter =>
                !StepSymbols.IsService(parameter) && !StepSymbols.IsCancellationToken(parameter)).ToArray()
            : callable.Parameters.Where(parameter =>
                !StepSymbols.IsService(parameter) && !StepSymbols.IsCancellationToken(parameter)).ToArray();
        RuntimeInputs = composite
            ? callable.Parameters.Skip(1).Where(parameter => !StepSymbols.IsCancellationToken(parameter)).ToArray()
            : Parameters;
        ExtensionTypeName = GeneratedNames.ExtensionTypeName(callable);
    }

    internal INamedTypeSymbol Type { get; }
    internal IMethodSymbol Callable { get; }
    internal string Name => Callable.Name;
    internal string Identity => Type.ToDisplayString() + "." + Callable.Name;
    internal ITypeSymbol? Result { get; }
    internal bool IsSynchronous { get; set; }
    internal bool IsComposite { get; }
    internal bool IsExternal { get; }
    internal string? AssemblyAlias { get; private set; }
    internal IReadOnlyList<string> AssemblyAliases { get; }
    internal bool HasLogger { get; }
    internal bool RequiresServices { get; set; }
    internal IParameterSymbol[] SpecialLoggerParameters { get; }
    internal IParameterSymbol[] Parameters { get; }
    internal IParameterSymbol[] RuntimeInputs { get; }
    internal string ExtensionTypeName { get; private set; }

    internal void UseExtensionTypeNames(IEnumerable<string> names)
    {
        var requested = names.Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length == 0) return;
        ExtensionTypeName = requested[0];
        _additionalExtensionTypeNames.Clear();
        _additionalExtensionTypeNames.AddRange(requested.Skip(1));
    }

    internal void UseAssemblyAlias(string alias) => AssemblyAlias = alias;

    internal Method CreateMethod(Compilation compilation)
    {
        var handle = Result is null
            ? GeneratedCode.Type(compilation.GetTypeByMetadataName(StepSymbols.VOID_BUILDER_NAME)!)
            : new DataType("global::TedToolkit.Orchestration.Pipeline.StepBuilder<" +
                TypeName(Result) + ">");
        var method = new Method(Name, new ReturnType(handle)).Public
            .AddRootDescription(Summary("Declares a step and binds its data inputs for the generator."));
        foreach (var parameter in Parameters)
        {
            var argument = new Parameter(new DataType(
                "global::TedToolkit.Orchestration.Pipeline.StepArgument<" +
                TypeName(parameter.Type) + ">"), parameter.Name);
            method.AddParameter(argument.AddDefault(SimpleNameExpression.Default));
        }
        return method;
    }

    internal StepFactory Rebind(Compilation compilation)
    {
        var assembly = compilation.Assembly.Identity.Equals(Type.ContainingAssembly.Identity)
            ? compilation.Assembly
            : compilation.SourceModule.ReferencedAssemblySymbols.Single(candidate =>
                candidate.Identity.Equals(Type.ContainingAssembly.Identity));
        var type = assembly.GetTypeByMetadataName(GeneratedNames.MetadataName(Type))!;
        if (IsComposite && IsExternal)
        {
            var declaration = type.GetMembers(Callable.Name).OfType<IMethodSymbol>().Single(candidate =>
                candidate.Parameters.Length == Callable.Parameters.Length &&
                PipelineSymbols.IsCompositeCandidate(candidate, compilation));
            var protocol = CompositeStepGenerator.FindProtocol(type, declaration, compilation,
                out var result, out var synchronous, out _)!;
            var rebound = new StepFactory(type, declaration, result!, synchronous, compilation,
                composite: true, external: true,
                requiresServices: CompositeStepGenerator.ProtocolRequiresServices(protocol, compilation),
                assemblyAliases: AssemblyAliases)
            {
                ExtensionTypeName = ExtensionTypeName,
            };
            rebound._additionalExtensionTypeNames.AddRange(_additionalExtensionTypeNames);
            return rebound;
        }

        var callable = IsComposite
            ? type.GetMembers(Callable.Name).OfType<IMethodSymbol>().Single(candidate =>
                candidate.Parameters.Length == Callable.Parameters.Length &&
                PipelineSymbols.IsCompositeCandidate(candidate, compilation))
            : type.GetMembers(Callable.Name).OfType<IMethodSymbol>().Single(candidate =>
                candidate.Parameters.Length == Callable.Parameters.Length && StepSymbols.IsLeafStep(candidate));
        var reboundResult = IsComposite
            ? type.GetTypeMembers(CompositeStepGenerator.ResultTypeName(callable)).Single()
            : StepSymbols.TryGetReturnShape(callable, out var leafResult, out _) ? leafResult : null;
        var reboundFactory = new StepFactory(type, callable, reboundResult, IsSynchronous, compilation, IsComposite,
            requiresServices: RequiresServices)
        {
            ExtensionTypeName = ExtensionTypeName,
        };
        reboundFactory._additionalExtensionTypeNames.AddRange(_additionalExtensionTypeNames);
        return reboundFactory;
    }

    internal string ExtensionMetadataName =>
        "TedToolkit.Orchestration.Pipeline." + ExtensionTypeName;

    internal bool IsSpecialLogger(IParameterSymbol parameter) =>
        SpecialLoggerParameters.Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate, parameter));

    internal string TypeName(ITypeSymbol type) => AssemblyAlias is null
        ? StepSymbols.TypeName(type)
        : StepSymbols.TypeName(type, Type.ContainingAssembly, AssemblyAlias);

    internal DataType GeneratedType(ITypeSymbol type) => new(TypeName(type));

    internal DataType GeneratedTaskType(ITypeSymbol? result) => result is null
        ? DataType.Task
        : DataType.TaskOf(GeneratedType(result));

    internal INamedTypeSymbol? ExistingExtensions =>
        Type.ContainingAssembly.GetTypeByMetadataName(ExtensionMetadataName);

    internal bool OwnsExtensionType(INamedTypeSymbol type, Compilation compilation)
    {
        if (ExistingExtensions is { } existing &&
            SymbolEqualityComparer.Default.Equals(type, existing)) return true;
        return ExtensionTypeNames().Any(name => SymbolEqualityComparer.Default.Equals(type,
            compilation.Assembly.GetTypeByMetadataName(
                "TedToolkit.Orchestration.Pipeline." + name)));
    }

    internal static string EmitExtensions(Compilation compilation,
        IReadOnlyList<StepFactory> factories, string builderMetadataName)
    {
        AssignExtensionTypeNames(factories);
        var declarations = new List<IMember>();
        var builderType = Type(compilation.GetTypeByMetadataName(builderMetadataName)!);
        foreach (var factory in factories)
        {
            declarations.Add(ExtensionDeclaration(
                compilation, factory, builderType, factory.ExtensionTypeName, true));
            foreach (var additional in factory._additionalExtensionTypeNames.Where(name =>
                name != factory.ExtensionTypeName))
                declarations.Add(ExtensionDeclaration(
                    compilation, factory, builderType, additional, false));
        }
        return AliasDirectives(factories) +
            Render("TedToolkit.Orchestration.Pipeline", declarations.ToArray());
    }

    private static TypeDeclaration ExtensionDeclaration(Compilation compilation,
        StepFactory factory, DataType builderType, string typeName, bool extension)
    {
        var declaration = new TypeDeclaration(
            typeName, TypeDeclarationType.CLASS).Internal.Static.Partial;
        var receiver = new Parameter(builderType, UniqueName("__builder", factory.Parameters));
        if (extension) _ = receiver.This;
        var method = factory.CreateMethod(compilation).Internal.Static;
        method.Parameters.Insert(0, receiver);
        method.AddStatement(SimpleNameExpression.Default.Return);
        declaration.AddMember(method);
        return declaration;
    }

    private IEnumerable<string> ExtensionTypeNames()
    {
        yield return ExtensionTypeName;
        foreach (var name in _additionalExtensionTypeNames) yield return name;
    }

    internal static string UniqueName(string proposed, IEnumerable<IParameterSymbol> parameters)
    {
        var names = new HashSet<string>(parameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
        while (names.Contains(proposed)) proposed += "_";
        return proposed;
    }

    private static bool IsNonGenericLogger(IParameterSymbol parameter, Compilation compilation)
    {
        var logger = compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILogger");
        return logger is not null && SymbolEqualityComparer.Default.Equals(parameter.Type, logger);
    }

    private static void AssignExtensionTypeNames(IReadOnlyList<StepFactory> factories)
    {
        foreach (var factory in Collisions(factories))
            factory.ExtensionTypeName = GeneratedNames.ExtensionTypeName(
                factory.Callable, includeNamespace: true);
        foreach (var factory in Collisions(factories))
            factory.ExtensionTypeName = GeneratedNames.ExtensionTypeName(
                factory.Callable, includeNamespace: true, includeAssembly: true);
        foreach (var factory in Collisions(factories))
            factory.ExtensionTypeName = GeneratedNames.DisambiguateIdentifier(
                factory.ExtensionTypeName, factory.Callable);
    }

    private static IEnumerable<StepFactory> Collisions(IReadOnlyList<StepFactory> factories) =>
        factories.GroupBy(factory => factory.ExtensionTypeName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).SelectMany(group => group);

    internal static string AliasDirectives(IEnumerable<StepFactory> factories) =>
        string.Concat(factories.Select(factory => factory.AssemblyAlias)
            .Where(alias => alias is not null).Distinct(StringComparer.Ordinal)
            .OrderBy(alias => alias, StringComparer.Ordinal)
            .Select(alias => "extern alias " + alias + ";\n"));

    internal static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol space)
                foreach (var type in Types(space)) yield return type;
            else if (member is INamedTypeSymbol type)
                yield return type;
        }
    }
}
