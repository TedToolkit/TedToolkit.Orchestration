using System;
using System.Collections.Generic;
using System.Linq;

using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;

using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Owns the generated typed factory surface for one constructible step.
internal sealed class StepFactory
{
    internal StepFactory(INamedTypeSymbol type, IMethodSymbol constructor, ITypeSymbol? result, bool synchronous,
        Compilation compilation, bool composite = false)
    {
        Type = type;
        Constructor = constructor;
        Result = result;
        IsSynchronous = synchronous;
        IsComposite = composite;
        IsExternal = !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);
        HasLogger = StepContextEmitter.HasAttribute(type, StepContextEmitter.LoggerAttributeName);
        HasContext = type.GetMembers("DisplayName").OfType<IPropertySymbol>().Any(property => property.IsRequired);
        RequiresServices = HasLogger || constructor.Parameters.Any(StepSymbols.IsService);
        Parameters = constructor.Parameters.Where(p => !StepSymbols.IsService(p)).ToArray();
        ExtensionTypeName = type.Name + "Extensions";
    }

    internal INamedTypeSymbol Type { get; }
    internal IMethodSymbol Constructor { get; }
    internal ITypeSymbol? Result { get; }
    internal bool IsSynchronous { get; set; }
    internal bool IsComposite { get; }
    internal bool IsExternal { get; }
    internal bool HasLogger { get; }
    internal bool HasContext { get; }
    internal bool RequiresServices { get; set; }
    internal IParameterSymbol[] Parameters { get; }
    internal string ExtensionTypeName { get; private set; }
    internal Method CreateMethod(Compilation compilation)
    {
        var handle = Result is null ? GeneratedCode.Type(compilation.GetTypeByMetadataName(StepSymbols.VoidBuilderName)!) : Runtime(compilation, "StepBuilder", Result);
        var method = new Method(Type.Name, new ReturnType(handle)).Public.AddRootDescription(Summary("Declares a step for the generator; omitted arguments become execution parameters."));
        foreach (var parameter in Parameters)
        {
            var argument = new Parameter(Runtime(compilation, "StepArgument", parameter.Type), parameter.Name);
            method.AddParameter(argument.AddDefault(SimpleNameExpression.Default));
        }
        return method;
    }
    internal StepFactory Rebind(Compilation compilation)
    {
        var type = compilation.GetTypeByMetadataName(Type.ToDisplayString())!;
        var constructor = type.InstanceConstructors.Single(candidate =>
            candidate.Parameters.Length == Constructor.Parameters.Length &&
            candidate.IsImplicitlyDeclared == Constructor.IsImplicitlyDeclared);
        return new StepFactory(type, constructor,
            IsComposite ? type.GetTypeMembers("Results").Single() : StepSymbols.ResultType(type, compilation),
            IsSynchronous, compilation, IsComposite)
        {
            RequiresServices = RequiresServices,
            ExtensionTypeName = ExtensionTypeName,
        };
    }

    internal string ExtensionMetadataName => "TedToolkit.Orchestration.Pipeline." + ExtensionTypeName;

    internal INamedTypeSymbol? ExistingExtensions => Type.ContainingAssembly.GetTypeByMetadataName(ExtensionMetadataName);

    internal INamedTypeSymbol? ExtensionsIn(Compilation compilation)
    {
        if (ExistingExtensions is { } existing &&
            compilation.IsSymbolAccessibleWithin(existing, compilation.Assembly))
            return existing;
        return compilation.Assembly.GetTypeByMetadataName(ExtensionMetadataName);
    }

    internal static string EmitExtensions(Compilation compilation, IReadOnlyList<StepFactory> factories,
        string builderMetadataName)
    {
        AssignExtensionTypeNames(factories);
        var declarations = new List<IMember>();
        var builderType = GeneratedCode.Type(compilation.GetTypeByMetadataName(builderMetadataName)!);
        foreach (var factory in factories)
        {
            if (!SymbolEqualityComparer.Default.Equals(factory.Type.ContainingAssembly, compilation.Assembly) &&
                factory.ExistingExtensions is { } existing &&
                compilation.IsSymbolAccessibleWithin(existing, compilation.Assembly)) continue;
            var extensions = new TypeDeclaration(factory.ExtensionTypeName, TypeDeclarationType.CLASS).Internal.Static.Partial;
            var receiver = "__builder";
            while (factory.Parameters.Any(parameter => parameter.Name == receiver)) receiver += "_";
            var method = factory.CreateMethod(compilation).Internal.Static;
            method.Parameters.Insert(0, new Parameter(builderType, receiver).This);
            method.AddStatement(SimpleNameExpression.Default.Return);
            extensions.AddMember(method);
            declarations.Add(extensions);
        }
        return Render("TedToolkit.Orchestration.Pipeline", declarations.ToArray());
    }

    private static void AssignExtensionTypeNames(IReadOnlyList<StepFactory> factories)
    {
        foreach (var group in factories.GroupBy(factory => factory.Type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            foreach (var factory in group)
                factory.ExtensionTypeName = Identifier(
                    factory.Type.ContainingNamespace.IsGlobalNamespace
                        ? "Global_" + factory.Type.Name + "Extensions"
                        : factory.Type.ContainingNamespace.ToDisplayString() + "_" +
                          factory.Type.Name + "Extensions");

            foreach (var collision in group.GroupBy(factory => factory.ExtensionTypeName, StringComparer.Ordinal)
                .Where(collision => collision.Count() > 1))
                foreach (var factory in collision)
                    factory.ExtensionTypeName = Identifier(
                        factory.Type.ContainingAssembly.Identity.Name + "_" + factory.ExtensionTypeName);
        }
    }

    private static string Identifier(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

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

