using System.Collections.Generic;
using System.Linq;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class ResultsEmitter
{
    internal static TypeDeclaration Create(IReadOnlyList<GraphNode> nodes)
    {
        var result = new TypeDeclaration("Results", TypeDeclarationType.STRUCT)
            .Public.Readonly
            .AddRootDescription(Summary("Contains the typed results produced by this Composite Step."));
        var constructor = new Constructor().Internal;
        foreach (var node in nodes.Where(node => node.Factory.Result is not null))
        {
            result.AddMember(new Property(Type(node.Factory.Result!), node.ResultName)
                .Public
                .AddRootDescription(Summary("Gets the completed result of this node."))
                .AddAccessor(new Accessor(AccessorType.GET)));
            constructor.AddParameter(new Parameter(Type(node.Factory.Result!), "value" + node.Index))
                .AddStatement(Name(node.ResultName.ToValidIdentifier())
                    .Assign(Name("value" + node.Index)));
        }
        if (nodes.Any(node => node.Factory.Result is not null))
            result.AddMember(constructor);
        return result;
    }
}
