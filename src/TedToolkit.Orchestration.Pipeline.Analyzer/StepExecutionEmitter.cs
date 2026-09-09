using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Each registration owns argument evaluation and fresh business step attempts.
internal static class StepExecutionEmitter
{
    private const string SupportType =
        "global::TedToolkit.Orchestration.Pipeline.CompilerServices.PipelineExecutionSupport";

    internal static string MethodName(GraphNode node, bool parallel = false) => "Run" + GraphReader.Capitalize(node.Name) + node.Index + (node.Factory.IsSynchronous && !parallel ? "" : "Async");

    internal static Method CreateRun(GraphNode node, bool parallel, bool staticCore = false,
        IReadOnlyList<IParameterSymbol>? rootInputs = null)
    {
        var factory = node.Factory;
        var policy = node.RetryCount != 0 || node.TimeoutMilliseconds != -1;
        var directTask = !parallel && !factory.IsSynchronous && !policy;
        var resultType = factory.IsSynchronous && !parallel ? (factory.Result is null ? DataType.Void : Type(factory.Result)) : TaskOf(factory.Result);
        var method = new Method(MethodName(node, parallel), new ReturnType(resultType)).Private;
        if (staticCore) _ = method.Static;
        method.IsAsync = !directTask && (parallel || !factory.IsSynchronous);
        if (staticCore)
        {
            if (factory.RequiresServices)
                method.AddParameter(new Parameter(typeof(System.IServiceProvider), "__services"));
            for (var index = 0; index < (rootInputs?.Count ?? 0); index++)
                method.AddParameter(new Parameter(Type(rootInputs![index].Type), "__root" + index));
        }
        for (var i = 0; i < node.Arguments.Count; i++)
            if (node.Arguments[i].Source is not null)
                method.AddParameter(new Parameter(parallel
                    ? TaskOf(node.Factory.Parameters[i].Type) : Type(node.Factory.Parameters[i].Type),
                    parallel ? "__dependency" + i : "__input" + i));
        if (parallel)
            foreach (var dependency in node.ControlDependencies)
                method.AddParameter(new Parameter(typeof(System.Threading.Tasks.Task), "__prerequisite" + dependency.Index));
        method.AddParameter(parallel
            ? new Parameter(typeof(System.Threading.CancellationTokenSource), "__execution")
            : new Parameter(typeof(System.Threading.CancellationToken), "executionToken"));

        if (parallel) method.AddStatement(Variable("executionToken", Name("__execution.Token")));
        if (parallel || !factory.IsComposite || factory.IsExternal)
            method.AddStatement(Call("executionToken.ThrowIfCancellationRequested"));
        if (parallel)
        {
            var dependencies = node.Arguments.Select((binding, index) => (binding, index)).Where(item => item.binding.Source is not null).ToArray();
            // Upstream tasks have already started independently in Execute. Await their values
            // directly; the outer WhenAll owns draining, so no join task or result array is needed.
            foreach (var dependency in dependencies)
                method.AddStatement(Variable("__input" + dependency.index, Await(Name("__dependency" + dependency.index))));
            foreach (var dependency in node.ControlDependencies)
                method.AddStatement(Await(Name("__prerequisite" + dependency.Index)));
            if (dependencies.Length != 0 || node.ControlDependencies.Count != 0)
                method.AddStatement(Call("executionToken.ThrowIfCancellationRequested"));
        }
        var arguments = PrepareArguments(method, node, staticCore);

        if (policy && factory.Result is not null)
            method.AddStatement(Variable("value", Name("default!"), Type(factory.Result)));
        IStatementOwner body = method;
        if (policy)
        {
            var lifetime = new UsingStatement(Variable("attempt", New(new DataType(
                    SupportType + ".StepAttempt"),
                node.RetryCount.ToLiteral(), node.TimeoutMilliseconds.ToLiteral(), Name("executionToken"))));
            method.AddStatement(lifetime);
            var loop = new GeneratedBlock(Call("attempt.Begin"));
            lifetime.AddStatement(loop);

            body = loop;
        }
        if (!policy && (parallel || factory.IsComposite && !factory.IsExternal))
            AppendAttempt(body, node, arguments, "executionToken", false, directTask, staticCore);
        else
        {
            var execution = new TryStatement();
            AppendAttempt(execution, node, arguments, policy ? "attempt.Token" : "executionToken", policy, directTask, staticCore);
            var failure = policy ? new CatchClause(typeof(System.Exception), "failure") : new CatchClause(typeof(System.Exception));
            if (policy)
                failure.AddStatement(Call("attempt.RetryOrThrow", Name("failure")));
            else
                failure.AddStatement(Call("executionToken.ThrowIfCancellationRequested")).AddStatement(new ThrowExpression());
            execution.AddCatch(failure);
            body.AddStatement(execution);

        }
        if (policy) method.AddStatement(factory.Result is null ? new ReturnStatement() : new ReturnStatement(Name("value")));
        if (parallel)
        {
            var guard = new TryStatement();
            guard.Statements.AddRange(method.Statements);
            method.Statements.Clear();
            var cancellation = new TryStatement();
            cancellation.AddStatement(Await(Call("__execution.CancelAsync")));
            cancellation.AddCatch(new CatchClause(typeof(System.Exception)));
            guard.AddCatch(new CatchClause(typeof(System.Exception))
                .AddStatement(cancellation).AddStatement(new ThrowExpression()));
            method.AddStatement(guard);
        }
        return method;
    }

    private static IExpression[] PrepareArguments(Method method, GraphNode node, bool staticCore)
    {
        var factory = node.Factory;
        foreach (var local in node.Locals)
            method.AddStatement(Variable(local.Name, local.Expression, local.Type is null ? null : Type(local.Type)));
        foreach (var binding in node.Arguments.Select((argument, index) => (argument, index))
            .Where(item => item.argument.Expression is not null && !item.argument.IsConstant).OrderBy(item => item.argument.Syntax!.SpanStart))
            method.AddStatement(Variable("__value" + binding.index, binding.argument.Expression!, Type(factory.Parameters[binding.index].Type)));
        var arguments = ConstructorArguments(node, staticCore).ToArray();
        for (var i = 0; i < factory.Constructor.Parameters.Length; i++)
            if (StepSymbols.IsService(factory.Constructor.Parameters[i]))
            {
                method.AddStatement(Variable("__service" + i, arguments[i], Type(factory.Constructor.Parameters[i].Type)));
                arguments[i] = Name("__service" + i);
            }

        if (factory.HasLogger)
        {
            var loggerFactoryType = new DataType("global::Microsoft.Extensions.Logging.ILoggerFactory");
            var loggerFactory = Call(
                "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService",
                Name(staticCore ? "__services" : "_services"),
                Call("typeof", loggerFactoryType.Type)).Cast(loggerFactoryType);
            method.AddStatement(Variable("__loggerFactory", loggerFactory, loggerFactoryType));
            method.AddStatement(Variable("__logger",
                Call(Name("__loggerFactory").Sub("CreateLogger"),
                    (factory.Type.ToDisplayString() + "[" + node.DisplayName + "]").ToLiteral()),
                new DataType("global::Microsoft.Extensions.Logging.ILogger")));
        }

        return arguments;
    }

    private static void AppendAttempt(IStatementOwner body, GraphNode node, IExpression[] arguments, string token,
        bool policy, bool directTask, bool staticCore)
    {
        var factory = node.Factory;
        // Begin checks the execution token; a new timeout token still needs its own check.
        // Otherwise recheck only after argument evaluation or service resolution.
        if ((!factory.IsComposite || factory.IsExternal) &&
            (policy ? node.TimeoutMilliseconds != -1 :
                node.Locals.Any() ||
                node.Arguments.Any(argument => argument.Expression is not null && !argument.IsConstant) ||
                factory.Constructor.Parameters.Any(StepSymbols.IsService)))
            body.AddStatement(Call(token + ".ThrowIfCancellationRequested"));
        var disposable = !factory.IsComposite && factory.IsSynchronous &&
            (factory.Type.GetMembers("Dispose").Length != 0 ||
             factory.Type.AllInterfaces.Any(type => type.ToDisplayString() == "System.IDisposable"));
        if (!policy && !directTask && factory.Result is not null)
            body.AddStatement(Variable("value", Name("default!"), Type(factory.Result)));
        IStatementOwner execution = body;
        IExpression call;
        var creation = New(Type(factory.Type), arguments);
        if (factory.HasContext)
            creation.AddVariable("DisplayName", node.DisplayName.ToLiteral());
        if (factory.HasLogger)
            creation.AddVariable("Logger", Name("__logger"));
        if (disposable)
        {
            var lifetime = new UsingStatement(Variable("step", creation));
            body.AddStatement(lifetime);
            execution = lifetime;
        }
        var target = (disposable ? Name("step") : (IExpression)creation)
            .Sub(factory.IsSynchronous ? "Execute" : "ExecuteAsync");
        call = factory.IsComposite && factory.RequiresServices
            ? Call(target, Name(staticCore ? "__services" : "_services"), Name(token))
            : Call(target, Name(token));
        if (directTask)
        {
            execution.AddStatement(Call(SupportType + ".ObserveStepAsync", call, Name(token)).Return);
            return;
        }
        if (!factory.IsSynchronous)
        {
            execution.AddStatement(Variable("operation", call));
            call = Await(Name("operation"));
        }
        execution.AddStatement(factory.Result is null ? call : Name("value").Assign(call));
        if (!factory.IsComposite || factory.IsExternal)
            body.AddStatement(Call(token + ".ThrowIfCancellationRequested"));
        if (!policy) body.AddStatement(factory.Result is null ? new ReturnStatement() : new ReturnStatement(Name("value")));
    }

    private static IEnumerable<IExpression> ConstructorArguments(GraphNode node, bool staticCore)
    {
        var argumentIndex = 0;
        foreach (var parameter in node.Factory.Constructor.Parameters)
        {
            if (StepSymbols.ServiceAttribute(parameter) is { } service)
            {
                var runtimeType = parameter.Type.TypeKind == TypeKind.Dynamic ? DataType.FromType(typeof(object)) :
                    Type(parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
                var key = service.ConstructorArguments.FirstOrDefault().Value as string;
                var resolve = key is null
                    ? Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService",
                        Name(staticCore ? "__services" : "_services"), Call("typeof", runtimeType.Type))
                    : Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService",
                        Name(staticCore ? "__services" : "_services"), Call("typeof", runtimeType.Type), key.ToLiteral());
                yield return resolve.Cast(Type(parameter.Type));
                continue;
            }
            var binding = node.Arguments[argumentIndex];
            yield return binding.Source is not null ? Name("__input" + argumentIndex) :
                binding.IsConstant ? binding.Expression! : Name("__value" + argumentIndex);
            argumentIndex++;
        }
    }
}
