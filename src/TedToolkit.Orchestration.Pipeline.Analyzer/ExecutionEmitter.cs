using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class ExecutionEmitter
{
    internal static Method Create(IReadOnlyList<GraphNode> nodes, bool discardResults,
        bool staticCore = false, IReadOnlyList<IParameterSymbol>? rootInputs = null,
        bool nestedCore = false, bool requiresServices = false)
    {
        rootInputs ??= Array.Empty<IParameterSymbol>();
        var asynchronous = nodes.Any(node => !node.Factory.IsSynchronous);
        var parallel = ExecutionPlan.IsParallel(nodes);
        var resultType = asynchronous
            ? discardResults ? DataType.Task : DataType.TaskOf(new DataType("Results"))
            : discardResults ? DataType.Void : new DataType("Results");
        var name = (discardResults ? "ExecuteWithoutResults" : "Execute") +
            (asynchronous ? "Async" : "") +
            (staticCore ? nestedCore ? "NestedCore" : "Core" : "");
        var method = new Method(name, new ReturnType(resultType))
            .AddRootDescription(Summary(discardResults
                ? "Executes all configured steps without collecting results."
                : "Executes configured steps and returns their typed results."));
        if (staticCore)
        {
            _ = method.Internal.Static;
            if (requiresServices)
                method.AddParameter(new Parameter(typeof(IServiceProvider), "__services"));
            for (var index = 0; index < rootInputs.Count; index++)
                method.AddParameter(new Parameter(Type(rootInputs[index].Type), "__root" + index));
        }
        else
        {
            _ = method.Public;
            foreach (var node in nodes)
                for (var index = 0; index < node.Arguments.Count; index++)
                    if (node.Arguments[index].Unbound)
                        method.AddParameter(new Parameter(
                            Type(node.Factory.Parameters[index].Type), node.Arguments[index].InputName));
        }
        method.IsAsync = asynchronous;
        method.AddParameter(new Parameter(typeof(System.Threading.CancellationToken), "cancellationToken")
            .AddDefault(SimpleNameExpression.Default));
        if (!nestedCore)
            method.AddStatement(Call("cancellationToken.ThrowIfCancellationRequested"));
        IStatementOwner body = method;
        if (parallel)
        {
            var lifetime = new UsingStatement(Variable("execution",
                Call("global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource",
                    Name("cancellationToken"))));
            method.AddStatement(lifetime);
            body = lifetime;
        }
        foreach (var node in nodes)
        {
            var inputs = node.Arguments.Where(binding => binding.Unbound || binding.Source is not null)
                .Select(binding => (IExpression)(binding.Source is { } source
                    ? Name((parallel ? "task" : "result") + source.Index)
                    : Name(binding.InputName))).ToList();
            if (parallel)
                inputs.AddRange(node.ControlDependencies.Select(dependency =>
                    (IExpression)Name("task" + dependency.Index).Cast(DataType.Task)));
            if (staticCore)
            {
                inputs.InsertRange(0, rootInputs.Select((_, index) => (IExpression)Name("__root" + index)));
                if (node.Factory.RequiresServices)
                    inputs.Insert(0, Name("__services"));
            }
            inputs.Add(Name(parallel ? "execution" : "cancellationToken"));
            IExpression call = Call(StepExecutionEmitter.MethodName(node, parallel), inputs.ToArray());
            if (parallel)
                body.AddStatement(Variable("task" + node.Index, call));
            else
            {
                if (!node.Factory.IsSynchronous) call = Await(call);
                body.AddStatement(node.Factory.Result is null
                    ? call
                    : Variable("result" + node.Index, call, Type(node.Factory.Result)));
            }
        }
        if (parallel)
            body.AddStatement(Await(Call("global::System.Threading.Tasks.Task.WhenAll",
                nodes.Select((node, index) => index == 0
                    ? (IExpression)Name("task" + node.Index).Cast(DataType.Task)
                    : Name("task" + node.Index)).ToArray())));
        if (parallel && !nestedCore)
            body.AddStatement(Call("cancellationToken.ThrowIfCancellationRequested"));
        var results = nodes.Where(node => node.Factory.Result is not null).Select(node => parallel
            ? (IExpression)Call(Call("task" + node.Index + ".GetAwaiter").Sub("GetResult"))
            : Name("result" + node.Index)).ToArray();
        body.AddStatement(discardResults
            ? new ReturnStatement()
            : new ReturnStatement(New(new DataType("Results"), results)));
        return method;
    }
}
