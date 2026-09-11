using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Each registration owns argument evaluation and direct business-function attempts.
internal static class StepExecutionEmitter
{
    private const string SupportType =
        "global::TedToolkit.Orchestration.Pipeline.CompilerServices.PipelineExecutionSupport";

    internal static string MethodName(GraphNode node, bool parallel = false) => "Run" + GraphReader.Capitalize(node.Name) + node.Index + (node.Factory.IsSynchronous && !parallel ? "" : "Async");

    internal static Method CreateRun(GraphNode node, bool parallel, bool staticCore = false,
        IReadOnlyList<IParameterSymbol>? rootInputs = null, bool hierarchicalDisplayPath = false)
    {
        var factory = node.Factory;
        var policy = node.RetryCount != 0 || node.TimeoutMilliseconds != -1;
        var directTask = !parallel && !factory.IsSynchronous && !policy;
        var resultType = factory.IsSynchronous && !parallel
            ? factory.Result is null ? DataType.Void : factory.GeneratedType(factory.Result)
            : factory.GeneratedTaskType(factory.Result);
        var method = new Method(MethodName(node, parallel), new ReturnType(resultType)).Private;
        if (staticCore) _ = method.Static;
        method.IsAsync = !directTask && (parallel || !factory.IsSynchronous);
        if (staticCore)
        {
            if (factory.RequiresServices)
                method.AddParameter(new Parameter(typeof(System.IServiceProvider), "__services"));
            if (hierarchicalDisplayPath)
                method.AddParameter(new Parameter(typeof(string), "__displayPath"));
            for (var index = 0; index < (rootInputs?.Count ?? 0); index++)
                method.AddParameter(new Parameter(Type(rootInputs![index].Type), "__root" + index));
        }
        for (var i = 0; i < node.Arguments.Count; i++)
            if (node.Arguments[i].Source is not null)
                method.AddParameter(new Parameter(parallel
                    ? factory.GeneratedTaskType(factory.Parameters[i].Type)
                    : factory.GeneratedType(factory.Parameters[i].Type),
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
        var arguments = PrepareArguments(method, node, staticCore, hierarchicalDisplayPath);

        if (policy && factory.Result is not null)
            method.AddStatement(Variable("value", Name("default!"), factory.GeneratedType(factory.Result)));
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
            cancellation.AddStatement(Await(Call(SupportType + ".CancelExecutionAsync", Name("__execution"))));
            cancellation.AddCatch(new CatchClause(typeof(System.Exception)));
            guard.AddCatch(new CatchClause(typeof(System.Exception))
                .AddStatement(cancellation).AddStatement(new ThrowExpression()));
            method.AddStatement(guard);
        }
        return method;
    }

    private static IExpression[] PrepareArguments(Method method, GraphNode node, bool staticCore,
        bool hierarchicalDisplayPath)
    {
        var factory = node.Factory;
        foreach (var local in node.Locals)
            method.AddStatement(Variable(local.Name, local.Expression, local.Type is null ? null : Type(local.Type)));
        foreach (var binding in node.Arguments.Select((argument, index) => (argument, index))
            .Where(item => item.argument.Expression is not null && !item.argument.IsConstant).OrderBy(item => item.argument.Syntax!.SpanStart))
            method.AddStatement(Variable("__value" + binding.index, binding.argument.Expression!,
                factory.GeneratedType(factory.Parameters[binding.index].Type)));
        string? stepPath = null;
        if (hierarchicalDisplayPath && (factory.IsComposite || factory.HasLogger))
        {
            stepPath = "__stepDisplayPath";
            method.AddStatement(Variable(stepPath,
                Name("__displayPath").Add("/".ToLiteral()).Add(node.DisplayName.ToLiteral()),
                new DataType("global::System.String")));
        }

        if (factory.IsComposite)
        {
            var compositeArguments = CompositeArguments(node).ToArray();
            var prepareArguments = new List<IExpression>();
            if (factory.RequiresServices)
                prepareArguments.Add(Name(staticCore ? "__services" : "_services"));
            prepareArguments.Add(Name(stepPath!));
            method.AddStatement(Variable("__compositeState", Call(
                Name(factory.TypeName(factory.Type)).Sub(CompositeStepGenerator.PrepareProtocolName),
                prepareArguments.ToArray())));
            return compositeArguments;
        }

        var arguments = InvocationArguments(node, staticCore).ToArray();
        for (var i = 0; i < factory.Callable.Parameters.Length; i++)
            if (StepSymbols.IsService(factory.Callable.Parameters[i]) &&
                !factory.IsSpecialLogger(factory.Callable.Parameters[i]))
            {
                method.AddStatement(Variable(
                    "__service" + i, arguments[i],
                    factory.GeneratedType(factory.Callable.Parameters[i].Type)));
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
                    hierarchicalDisplayPath
                        ? (factory.Identity + "[").ToLiteral().Add(Name(stepPath!)).Add("]".ToLiteral())
                        : (factory.Identity + "[" + node.DisplayName + "]").ToLiteral()),
                new DataType("global::Microsoft.Extensions.Logging.ILogger")));
            foreach (var logger in factory.SpecialLoggerParameters)
                arguments[logger.Ordinal] = Name("__logger");
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
                factory.Callable.Parameters.Any(StepSymbols.IsService)))
            body.AddStatement(Call(token + ".ThrowIfCancellationRequested"));
        if (!policy && !directTask && factory.Result is not null)
            body.AddStatement(Variable("value", Name("default!"), factory.GeneratedType(factory.Result)));
        IExpression call;
        if (factory.IsComposite)
        {
            var compositeArguments = new List<IExpression> { Name("__compositeState") };
            compositeArguments.AddRange(arguments);
            compositeArguments.Add(Name(token));
            call = Call(Name(factory.TypeName(factory.Type)).Sub(
                CompositeStepGenerator.ExecuteProtocolName), compositeArguments.ToArray());
        }
        else
        {
            arguments[factory.Callable.Parameters.Length - 1] = Name(token);
            call = Call(
                Name(factory.TypeName(factory.Type)).Sub(factory.Callable.Name),
                arguments);
        }
        if (directTask)
        {
            body.AddStatement(Call(SupportType + ".ObserveStepAsync", call, Name(token)).Return);
            return;
        }
        if (!factory.IsSynchronous)
        {
            body.AddStatement(Variable("operation", call));
            call = Await(Name("operation"));
        }
        body.AddStatement(factory.Result is null ? call : Name("value").Assign(call));
        if (!factory.IsComposite || factory.IsExternal)
            body.AddStatement(Call(token + ".ThrowIfCancellationRequested"));
        if (!policy) body.AddStatement(factory.Result is null ? new ReturnStatement() : new ReturnStatement(Name("value")));
    }

    private static IEnumerable<IExpression> InvocationArguments(GraphNode node, bool staticCore)
    {
        var factory = node.Factory;
        var argumentIndex = 0;
        foreach (var parameter in factory.Callable.Parameters)
        {
            if (!node.Factory.IsComposite && StepSymbols.IsCancellationToken(parameter))
            {
                yield return Name("executionToken");
                continue;
            }
            if (StepSymbols.ServiceAttribute(parameter) is { } service)
            {
                if (factory.IsSpecialLogger(parameter))
                {
                    yield return Name("__logger");
                    continue;
                }
                var runtimeType = ServiceLookupType(parameter.Type, factory.GeneratedType);
                var key = service.ConstructorArguments.FirstOrDefault().Value as string;
                var resolve = key is null
                    ? Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService",
                        Name(staticCore ? "__services" : "_services"), Call("typeof", runtimeType.Type))
                    : Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService",
                        Name(staticCore ? "__services" : "_services"), Call("typeof", runtimeType.Type), key.ToLiteral());
                yield return resolve.Cast(factory.GeneratedType(parameter.Type));
                continue;
            }
            var binding = node.Arguments[argumentIndex];
            yield return binding.Source is not null ? Name("__input" + argumentIndex) :
                binding.IsConstant ? binding.Expression! : Name("__value" + argumentIndex);
            argumentIndex++;
        }
    }

    private static IEnumerable<IExpression> CompositeArguments(GraphNode node)
    {
        for (var index = 0; index < node.Factory.Parameters.Length; index++)
        {
            var binding = node.Arguments[index];
            yield return binding.Source is not null ? Name("__input" + index) :
                binding.IsConstant ? binding.Expression! : Name("__value" + index);
        }
    }
}
