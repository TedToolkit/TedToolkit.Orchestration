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
    internal static Method Create(IMethodSymbol declaration, IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<IParameterSymbol> rootInputs, bool requiresServices, bool isPublic)
    {
        var asynchronous = nodes.Any(node => !node.Factory.IsSynchronous);
        var parallel = ExecutionPlan.IsParallel(nodes);
        var resultType = new DataType(CompositeStepGenerator.ResultTypeName(declaration));
        var method = new Method(declaration.Name, new ReturnType(
                asynchronous ? DataType.TaskOf(resultType) : resultType)).Static
            .AddRootDescription(Summary("Executes this Composite Step and returns its typed results."));
        _ = isPublic ? method.Public : method.Internal;
        var editorBrowsable = new TedToolkit.RoslynHelper.Syntaxes.Attribute(new DataType(
            "global::System.ComponentModel.EditorBrowsableAttribute"));
        editorBrowsable.Arguments.Add(new Argument(Name(
            "global::System.ComponentModel.EditorBrowsableState.Never")));
        method.AddAttribute(editorBrowsable);
        method.IsAsync = asynchronous;

        var declaredToken = declaration.Parameters.FirstOrDefault(StepSymbols.IsCancellationToken);
        var names = new GeneratedNameAllocator(rootInputs.Select(parameter => parameter.Name)
            .Concat(declaredToken is null ? Array.Empty<string>() : new[] { declaredToken.Name }));
        var servicesName = requiresServices ? names.Allocate("__services") : null;
        var displayPathName = names.Allocate("__displayPath");
        if (requiresServices)
            method.AddParameter(new Parameter(typeof(IServiceProvider), servicesName!));
        method.AddParameter(new Parameter(typeof(string), displayPathName));
        foreach (var parameter in rootInputs)
        {
            var projected = new Parameter(Type(parameter.Type), parameter.Name);
            if (StepSymbols.IsService(parameter))
            {
                var marker = new TedToolkit.RoslynHelper.Syntaxes.Attribute(new DataType(
                    "global::TedToolkit.Orchestration.Pipeline.Attributes.FromServicesAttribute"));
                if (StepSymbols.ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value is string key)
                    marker.Arguments.Add(new Argument(key.ToLiteral()));
                projected.AddAttribute(marker);
            }
            if (parameter.HasExplicitDefaultValue)
                projected.AddDefault(ExplicitDefault(parameter));
            method.AddParameter(projected);
        }
        var tokenName = declaredToken?.Name ?? names.Allocate("cancellationToken");
        method.AddParameter(new Parameter(typeof(System.Threading.CancellationToken), tokenName)
            .AddDefault(SimpleNameExpression.Default));
        method.AddStatement(Call(tokenName + ".ThrowIfCancellationRequested"));
        var executionName = parallel ? names.Allocate("execution") : null;
        var nodeValueNames = parallel
            ? nodes.ToDictionary(node => node.Index,
                node => names.Allocate("task" + node.Index))
            : nodes.Where(node => node.Factory.Result is not null).ToDictionary(
                node => node.Index, node => names.Allocate("result" + node.Index));
        IStatementOwner body = method;
        if (parallel)
        {
            var lifetime = new UsingStatement(Variable(executionName!,
                Call("global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource",
                    Name(tokenName))));
            method.AddStatement(lifetime);
            body = lifetime;
        }
        foreach (var node in nodes)
        {
            var inputs = node.Arguments.Where(binding => binding.Source is not null)
                .Select(binding => (IExpression)Name(nodeValueNames[binding.Source!.Index])).ToList();
            if (parallel)
                inputs.AddRange(node.ControlDependencies.Select(dependency =>
                    (IExpression)Name(nodeValueNames[dependency.Index]).Cast(DataType.Task)));
            inputs.InsertRange(0, rootInputs.Select(parameter => (IExpression)Name(parameter.Name)));
            if (node.Factory.RequiresServices)
                inputs.Insert(0, Name(servicesName!));
            var displayInsertion = node.Factory.RequiresServices ? 1 : 0;
            inputs.Insert(displayInsertion, Name(displayPathName));
            inputs.Add(Name(parallel ? executionName! : tokenName));
            IExpression call = Call(
                StepExecutionEmitter.MethodName(node, parallel, declaration.Name), inputs.ToArray());
            if (parallel)
                body.AddStatement(Variable(nodeValueNames[node.Index], call));
            else
            {
                if (!node.Factory.IsSynchronous) call = Await(call);
                body.AddStatement(node.Factory.Result is null
                    ? call
                    : Variable(nodeValueNames[node.Index], call,
                        node.Factory.GeneratedType(node.Factory.Result)));
            }
        }
        if (parallel)
            body.AddStatement(Await(Call("global::System.Threading.Tasks.Task.WhenAll",
                nodes.Select((node, index) => index == 0
                    ? (IExpression)Name(nodeValueNames[node.Index]).Cast(DataType.Task)
                    : Name(nodeValueNames[node.Index])).ToArray())));
        if (parallel)
            body.AddStatement(Call(tokenName + ".ThrowIfCancellationRequested"));
        var results = nodes.Where(node => node.Factory.Result is not null).Select(node => parallel
            ? (IExpression)Call(Call(nodeValueNames[node.Index] + ".GetAwaiter").Sub("GetResult"))
            : Name(nodeValueNames[node.Index])).ToArray();
        body.AddStatement(new ReturnStatement(New(resultType, results)));
        return method;
    }
}
