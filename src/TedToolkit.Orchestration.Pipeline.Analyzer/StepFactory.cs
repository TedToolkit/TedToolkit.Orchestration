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
    internal StepFactory(INamedTypeSymbol type, IMethodSymbol callable, ITypeSymbol? result,
        bool synchronous, Compilation compilation, bool composite = false,
        bool external = false, bool requiresServices = false, string? assemblyAlias = null)
    {
        Type = type;
        Callable = callable;
        Result = result;
        IsSynchronous = synchronous;
        IsComposite = composite;
        IsExternal = external;
        AssemblyAlias = assemblyAlias;
        SpecialLoggerParameters = external ? Array.Empty<IParameterSymbol>() : callable.Parameters
            .Where(parameter => IsNonGenericLogger(parameter, compilation) &&
                StepSymbols.ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value is null)
            .ToArray();
        HasLogger = SpecialLoggerParameters.Length != 0;
        RequiresServices = requiresServices || HasLogger || callable.Parameters.Any(StepSymbols.IsService);
        Parameters = composite
            ? external
                ? callable.Parameters.Skip(1).Take(callable.Parameters.Length - 2).ToArray()
                : callable.Parameters.Skip(1).Where(parameter =>
                    !StepSymbols.IsService(parameter) && !StepSymbols.IsCancellationToken(parameter)).ToArray()
            : callable.Parameters.Where(parameter =>
                !StepSymbols.IsService(parameter) && !StepSymbols.IsCancellationToken(parameter)).ToArray();
        RuntimeInputs = composite && !external
            ? callable.Parameters.Skip(1).Where(parameter => !StepSymbols.IsCancellationToken(parameter)).ToArray()
            : Parameters;
        ExtensionTypeName = composite
            ? GeneratedNames.ExtensionTypeName(type)
            : GeneratedNames.ExtensionTypeName(callable);
    }

    internal INamedTypeSymbol Type { get; }
    internal IMethodSymbol Callable { get; }
    internal string Name => IsComposite ? Type.Name : Callable.Name;
    internal string Identity => IsComposite
        ? Type.ToDisplayString() + ".Configuration"
        : Type.ToDisplayString() + "." + Callable.Name;
    internal ITypeSymbol? Result { get; }
    internal bool IsSynchronous { get; set; }
    internal bool IsComposite { get; }
    internal bool IsExternal { get; }
    internal string? AssemblyAlias { get; }
    internal bool HasLogger { get; }
    internal bool RequiresServices { get; set; }
    internal IParameterSymbol[] SpecialLoggerParameters { get; }
    internal IParameterSymbol[] Parameters { get; }
    internal IParameterSymbol[] RuntimeInputs { get; }
    internal string ExtensionTypeName { get; private set; }

    internal Method CreateMethod(Compilation compilation)
    {
        var handle = Result is null
            ? GeneratedCode.Type(compilation.GetTypeByMetadataName(StepSymbols.VoidBuilderName)!)
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
            var protocol = type.GetMembers(CompositeStepGenerator.ExecuteProtocolName)
                .OfType<IMethodSymbol>().Single(CompositeStepGenerator.IsProtocolMethod);
            var result = CompositeStepGenerator.ProtocolResult(protocol, out var synchronous);
            return new StepFactory(type, protocol, result, synchronous, compilation,
                composite: true, external: true, requiresServices: RequiresServices,
                assemblyAlias: AssemblyAlias)
            {
                ExtensionTypeName = ExtensionTypeName,
            };
        }

        var callable = IsComposite
            ? type.GetMembers("Configuration").OfType<IMethodSymbol>().Single(candidate =>
                candidate.Parameters.Length == Callable.Parameters.Length &&
                PipelineSymbols.IsCompositeCandidate(candidate, compilation))
            : type.GetMembers(Callable.Name).OfType<IMethodSymbol>().Single(candidate =>
                candidate.Parameters.Length == Callable.Parameters.Length && StepSymbols.IsLeafStep(candidate));
        var reboundResult = IsComposite
            ? type.GetTypeMembers("Results").Single()
            : StepSymbols.TryGetReturnShape(callable, out var leafResult, out _) ? leafResult : null;
        return new StepFactory(type, callable, reboundResult, IsSynchronous, compilation, IsComposite,
            requiresServices: RequiresServices)
        {
            ExtensionTypeName = ExtensionTypeName,
        };
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

    internal INamedTypeSymbol? ExtensionsIn(Compilation compilation)
    {
        if (ExistingExtensions is { } existing &&
            compilation.IsSymbolAccessibleWithin(existing, compilation.Assembly))
            return existing;
        return compilation.Assembly.GetTypeByMetadataName(ExtensionMetadataName);
    }

    internal static string EmitExtensions(Compilation compilation,
        IReadOnlyList<StepFactory> factories, string builderMetadataName)
    {
        AssignExtensionTypeNames(factories);
        var declarations = new List<IMember>();
        var builderType = Type(compilation.GetTypeByMetadataName(builderMetadataName)!);
        foreach (var factory in factories)
        {
            var extensions = new TypeDeclaration(
                factory.ExtensionTypeName, TypeDeclarationType.CLASS).Internal.Static.Partial;
            var receiver = UniqueName("__builder", factory.Parameters);
            var method = factory.CreateMethod(compilation).Internal.Static;
            method.Parameters.Insert(0, new Parameter(builderType, receiver).This);
            method.AddStatement(SimpleNameExpression.Default.Return);
            extensions.AddMember(method);
            declarations.Add(extensions);
        }
        return AliasDirectives(factories) +
            Render("TedToolkit.Orchestration.Pipeline", declarations.ToArray());
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
            factory.ExtensionTypeName = factory.IsComposite
                ? GeneratedNames.ExtensionTypeName(factory.Type, includeNamespace: true)
                : GeneratedNames.ExtensionTypeName(factory.Callable, includeNamespace: true);
        foreach (var factory in Collisions(factories))
            factory.ExtensionTypeName = factory.IsComposite
                ? GeneratedNames.ExtensionTypeName(factory.Type, includeNamespace: true, includeAssembly: true)
                : GeneratedNames.ExtensionTypeName(factory.Callable, includeNamespace: true, includeAssembly: true);
        foreach (var factory in Collisions(factories))
            factory.ExtensionTypeName = factory.IsComposite
                ? GeneratedNames.DisambiguateIdentifier(factory.ExtensionTypeName, factory.Type)
                : GeneratedNames.DisambiguateIdentifier(factory.ExtensionTypeName, factory.Callable);
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
