using System.Collections.Generic;
using System.Linq;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Only decides whether the graph needs concurrent task composition; no execution segments.
internal static class ExecutionPlan
{
    internal static bool IsParallel(IReadOnlyList<GraphNode> nodes)
    {
        if (nodes.All(node => node.Factory.IsSynchronous)) return false;
        var ancestors = new Dictionary<GraphNode, HashSet<GraphNode>>();
        foreach (var node in nodes)
        {
            var upstream = node.Arguments.Select(argument => argument.Source).OfType<GraphNode>().Distinct().ToArray();
            var reachable = new HashSet<GraphNode>(upstream);
            foreach (var dependency in upstream) reachable.UnionWith(ancestors[dependency]);
            if (ancestors.Keys.Any(previous => !reachable.Contains(previous))) return true;
            ancestors.Add(node, reachable);
        }
        return false;
    }
}
