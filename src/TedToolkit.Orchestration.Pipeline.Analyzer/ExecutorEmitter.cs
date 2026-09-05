using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;

using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class ExecutorEmitter
{
    internal static string Emit(Compilation compilation, INamedTypeSymbol owner, MethodDeclarationSyntax configure,
        IReadOnlyList<GraphNode> nodes)
    {
        var declaration = Declaration(owner)
            .AddMember(new Field(typeof(IServiceProvider), "_services").Private.Readonly);
        var method = (IMethodSymbol)compilation.GetSemanticModel(configure.SyntaxTree).GetDeclaredSymbol(configure)!;
        var servicesName = "services";
        while (method.Parameters.Any(parameter => parameter.Name == servicesName)) servicesName += "_";
        var constructor = new Constructor().Public.AddRootDescription(Summary("Stores the service provider and explicit constructor inputs without running configuration.")).AddParameter(new Parameter(typeof(IServiceProvider), servicesName))
            .AddStatement(Call("global::System.ArgumentNullException.ThrowIfNull", Name(servicesName)))
            .AddStatement(Name("this._services").Assign(Name(servicesName)));
        foreach (var parameter in method.Parameters.Skip(1))
        {
            constructor.AddParameter(new Parameter(Type(parameter.Type), parameter.Name));
            var field = "_configuration" + (parameter.Ordinal - 1);
            declaration.AddMember(new Field(Type(parameter.Type), field).Private.Readonly);
            constructor.AddStatement(Name("this." + field).Assign(Name(parameter.Name.ToValidIdentifier())));
        }
        declaration.AddMember(constructor);
        var parallel = ExecutionPlan.IsParallel(nodes);
        foreach (var node in nodes) declaration.AddMember(StepExecutionEmitter.CreateRun(node, parallel));
        declaration.AddMember(Results(nodes));
        declaration.AddMember(ExecutionEmitter.Create(nodes, false));
        declaration.AddMember(ExecutionEmitter.Create(nodes, true));
        return RenderConfiguration(compilation, Namespace(owner), configure, declaration);
    }

    private static TypeDeclaration Results(IReadOnlyList<GraphNode> nodes)
    {
        var result = new TypeDeclaration("Results", TypeDeclarationType.STRUCT).Public.Readonly.AddRootDescription(Summary("Generated strongly typed pipeline configuration or results."));
        var constructor = new Constructor().Internal;
        foreach (var node in nodes.Where(node => node.Factory.Result is not null))
        {
            result.AddMember(new Property(Type(node.Factory.Result!), node.ResultName).Public.AddRootDescription(Summary("Gets the completed result of this node.")).AddAccessor(new Accessor(AccessorType.GET)));
            constructor.AddParameter(new Parameter(Type(node.Factory.Result!), "value" + node.Index))
                .AddStatement(Name(node.ResultName.ToValidIdentifier()).Assign(Name("value" + node.Index)));
        }
        if (nodes.Any(node => node.Factory.Result is not null)) result.AddMember(constructor);
        return result;
    }
    private static string Namespace(INamedTypeSymbol owner) => owner.ContainingNamespace.IsGlobalNamespace ? "" : owner.ContainingNamespace.ToDisplayString();
    private static TypeDeclaration Declaration(INamedTypeSymbol owner)
    {
        var declaration = new TypeDeclaration(owner.Name, TypeDeclarationType.CLASS).Partial;
        return owner.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public ? declaration.Public : declaration.Internal;
    }
}
