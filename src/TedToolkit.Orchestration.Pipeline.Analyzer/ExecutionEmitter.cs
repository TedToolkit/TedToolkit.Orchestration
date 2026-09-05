using System.Collections.Generic;
using System.Linq;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class ExecutionEmitter
{
    internal static Method Create(IReadOnlyList<GraphNode> nodes, bool discardResults)
    {
        var asynchronous = nodes.Any(node => !node.Factory.IsSynchronous);
        var parallel = ExecutionPlan.IsParallel(nodes);
        var resultType = asynchronous ? (discardResults ? DataType.Task : DataType.TaskOf(new DataType("Results"))) : (discardResults ? DataType.Void : new DataType("Results"));
        var method = new Method((discardResults ? "ExecuteWithoutResults" : "Execute") + (asynchronous ? "Async" : ""), new ReturnType(resultType)).Public
            .AddRootDescription(Summary(discardResults ? "Executes all configured steps without collecting results." : "Executes all configured steps and returns their typed results."));
        method.IsAsync = asynchronous;
        foreach (var node in nodes)
            for (var i = 0; i < node.Arguments.Count; i++)
                if (node.Arguments[i].Unbound)
                    method.AddParameter(new Parameter(Type(node.Factory.Parameters[i].Type), node.Arguments[i].InputName));
        method.AddParameter(new Parameter(typeof(System.Threading.CancellationToken), "cancellationToken").AddDefault(SimpleNameExpression.Default));
        method.AddStatement(Call("cancellationToken.ThrowIfCancellationRequested"));
        IStatementOwner body = method;
        if (parallel)
        {
            var lifetime = new UsingStatement(Variable("execution", Call("global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource", Name("cancellationToken"))));
            method.AddStatement(lifetime);
            body = lifetime;
        }
        foreach (var node in nodes)
        {
            var inputs = node.Arguments.Where(binding => binding.Unbound || binding.Source is not null)
                .Select(binding => (IExpression)(binding.Source is { } source
                    ? Name((parallel ? "task" : "result") + source.Index) : Name(binding.InputName)));
            IExpression call = Call(StepExecutionEmitter.MethodName(node, parallel),
                inputs.Concat(new[] { Name(parallel ? "execution" : "cancellationToken") }).ToArray());
            if (parallel)
                body.AddStatement(Variable("task" + node.Index, call));
            else
            {
                if (!node.Factory.IsSynchronous) call = Await(call);
                body.AddStatement(node.Factory.Result is null ? call : Variable("result" + node.Index, call, Type(node.Factory.Result)));

            }
        }
        if (parallel)
            body.AddStatement(Await(Call("global::System.Threading.Tasks.Task.WhenAll", nodes.Select((node, index) => index == 0 ? (IExpression)Name("task" + node.Index).Cast(DataType.Task) : Name("task" + node.Index)).ToArray())));
        if (parallel) body.AddStatement(Call("cancellationToken.ThrowIfCancellationRequested"));
        // WhenAll succeeded before any parallel result is read; never block on pending tasks.
        var results = nodes.Where(node => node.Factory.Result is not null).Select(node => parallel
            ? (IExpression)Call(Call("task" + node.Index + ".GetAwaiter").Sub("GetResult"))
            : Name("result" + node.Index)).ToArray();
        body.AddStatement(discardResults ? new ReturnStatement() : new ReturnStatement(New(new DataType("Results"), results)));
        return method;
    }
}
