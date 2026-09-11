using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;
using Accessibility = Microsoft.CodeAnalysis.Accessibility;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Emits one static Composite protocol and its optional root facade.
internal static class CompositeStepEmitter
{
    internal static string Emit(Compilation compilation, IMethodSymbol configuration,
        MethodDeclarationSyntax syntax, IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<IParameterSymbol> runtimeInputs, bool requiresServices, bool emitPipeline)
    {
        var owner = configuration.ContainingType;
        var isPublic = owner.DeclaredAccessibility == Accessibility.Public &&
            configuration.DeclaredAccessibility == Accessibility.Public;
        var asynchronous = nodes.Any(node => !node.Factory.IsSynchronous);
        var declaration = new TypeDeclaration(owner.Name, TypeDeclarationType.CLASS).Static.Partial;
        _ = owner.DeclaredAccessibility == Accessibility.Public ? declaration.Public : declaration.Internal;

        foreach (var node in nodes)
            declaration.AddMember(StepExecutionEmitter.CreateRun(node,
                ExecutionPlan.IsParallel(nodes), true, runtimeInputs, hierarchicalDisplayPath: true,
                ownerMethod: configuration.Name));
        declaration.AddMember(ResultsEmitter.Create(
            CompositeStepGenerator.ResultTypeName(configuration), nodes, isPublic));
        declaration.AddMember(ExecutionEmitter.Create(
            configuration, nodes, runtimeInputs, requiresServices, isPublic));
        if (emitPipeline)
            declaration.AddMember(PipelineFacadeEmitter.CompositeFacade(
                configuration, asynchronous, requiresServices));

        var nameSpace = owner.ContainingNamespace.IsGlobalNamespace
            ? "" : GeneratedNames.Namespace(owner.ContainingNamespace);
        return StepFactory.AliasDirectives(nodes.Select(node => node.Factory)) +
            RenderConfiguration(compilation, nameSpace, syntax, declaration);
    }

}
