using System.Collections.Generic;
using System.Linq;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class ResultsEmitter
{
    internal static TypeDeclaration Create(IReadOnlyList<GraphNode> nodes, bool isPublic = true)
    {
        var result = new TypeDeclaration("Results", TypeDeclarationType.STRUCT)
            .Readonly
            .AddRootDescription(Summary("Contains the typed results produced by this Composite Step."));
        _ = isPublic ? result.Public : result.Internal;
        var constructor = new Constructor().Internal;
        foreach (var node in nodes.Where(node => node.Factory.Result is not null))
        {
            result.AddMember(new Property(node.Factory.GeneratedType(node.Factory.Result!), node.ResultName)
                .Public
                .AddRootDescription(Summary("Gets the completed result of this node."))
                .AddAccessor(new Accessor(AccessorType.GET)));
            constructor.AddParameter(new Parameter(
                node.Factory.GeneratedType(node.Factory.Result!), "value" + node.Index))
                .AddStatement(Name(node.ResultName.ToValidIdentifier())
                    .Assign(Name("value" + node.Index)));
        }
        if (nodes.Any(node => node.Factory.Result is not null))
            result.AddMember(constructor);
        return result;
    }
}
