using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Emits one ref-like graph value plus its conventional DI/root facade.
internal static class CompositeStepEmitter
{
    internal static string Emit(Compilation compilation, INamedTypeSymbol owner,
        MethodDeclarationSyntax configuration, IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<IParameterSymbol> inputs, bool requiresServices)
    {
        var asynchronous = nodes.Any(node => !node.Factory.IsSynchronous);
        var declaration = new TypeDeclaration(owner.Name, TypeDeclarationType.REF_STRUCT).Readonly.Partial;
        _ = owner.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public
            ? declaration.Public
            : declaration.Internal;
        var resultName = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".Results";
        declaration.AddBaseType(new DataType(asynchronous
            ? "global::TedToolkit.Orchestration.Pipeline.IAsyncCompositeStep<" + resultName + ">"
            : "global::TedToolkit.Orchestration.Pipeline.ICompositeStep<" + resultName + ">"));

        var parallel = ExecutionPlan.IsParallel(nodes);
        foreach (var node in nodes)
            declaration.AddMember(StepExecutionEmitter.CreateRun(node, parallel, true, inputs));
        declaration.AddMember(ResultsEmitter.Create(nodes));
        declaration.AddMember(ExecutionEmitter.Create(nodes, false, true, inputs, true, requiresServices));
        declaration.AddMember(ExecutionEmitter.Create(nodes, false, true, inputs, false, requiresServices));
        declaration.AddMember(ExecutionEmitter.Create(nodes, true, true, inputs, false, requiresServices));
        declaration.AddMember(Forwarder(owner, inputs, asynchronous, false, requiresServices));
        declaration.AddMember(Forwarder(owner, inputs, asynchronous, true, requiresServices));
        declaration.AddMember(Facade(owner, inputs, asynchronous, requiresServices,
            StepContextEmitter.HasAttribute(owner, StepContextEmitter.LoggerAttributeName)));

        var nameSpace = owner.ContainingNamespace.IsGlobalNamespace
            ? ""
            : owner.ContainingNamespace.ToDisplayString();
        return RenderConfiguration(compilation, nameSpace, configuration, declaration);
    }

    private static Method Forwarder(INamedTypeSymbol owner, IReadOnlyList<IParameterSymbol> inputs,
        bool asynchronous, bool discardResults, bool requiresServices)
    {
        var methodName = (discardResults ? "ExecuteWithoutResults" : "Execute") +
            (asynchronous ? "Async" : "");
        var resultType = asynchronous
            ? discardResults ? DataType.Task : DataType.TaskOf(new DataType("Results"))
            : discardResults ? DataType.Void : new DataType("Results");
        var method = new Method(methodName, new ReturnType(resultType)).Public
            .AddRootDescription(Summary(discardResults
                ? "Executes this Composite Step without collecting results."
                : "Executes this Composite Step and returns its typed results."));
        var providerName = "__services";
        while (inputs.Any(input => input.Name == providerName)) providerName += "_";
        if (requiresServices)
            method.AddParameter(new Parameter(typeof(IServiceProvider), providerName));
        method.AddParameter(new Parameter(typeof(System.Threading.CancellationToken), "cancellationToken")
            .AddDefault(SimpleNameExpression.Default));
        if (requiresServices)
            method.AddStatement(Call("global::System.ArgumentNullException.ThrowIfNull", Name(providerName)));
        var arguments = new List<IExpression>();
        if (requiresServices)
            arguments.Add(Name(providerName));
        arguments.AddRange(inputs.Select(parameter => (IExpression)Name(parameter.Name.ToValidIdentifier())));
        arguments.Add(Name("cancellationToken"));
        var call = Call(methodName + "Core", arguments.ToArray());
        if (discardResults && !asynchronous) method.AddStatement(call);
        else method.AddStatement(call.Return);
        return method;
    }

    private static TypeDeclaration Facade(INamedTypeSymbol owner, IReadOnlyList<IParameterSymbol> inputs,
        bool asynchronous, bool requiresServices, bool hasLogger)
    {
        var facade = new TypeDeclaration("Pipeline", TypeDeclarationType.CLASS).Public.Sealed
            .AddRootDescription(Summary("Provides a reusable root execution facade for this Composite Step."));
        if (requiresServices)
        {
            facade.AddMember(new Field(typeof(IServiceProvider), "_services").Private.Readonly);
            facade.AddMember(new Constructor().Public
                .AddRootDescription(Summary("Creates a root facade using the supplied service provider."))
                .AddParameter(new Parameter(typeof(IServiceProvider), "services"))
                .AddStatement(Call("global::System.ArgumentNullException.ThrowIfNull", Name("services")))
                .AddStatement(Name("_services").Assign(Name("services"))));
        }

        facade.AddMember(FacadeMethod(owner, inputs, asynchronous, false, requiresServices, hasLogger));
        facade.AddMember(FacadeMethod(owner, inputs, asynchronous, true, requiresServices, hasLogger));
        return facade;
    }

    private static Method FacadeMethod(INamedTypeSymbol owner, IReadOnlyList<IParameterSymbol> inputs,
        bool asynchronous, bool discardResults, bool requiresServices, bool hasLogger)
    {
        var methodName = (discardResults ? "ExecuteWithoutResults" : "Execute") +
            (asynchronous ? "Async" : "");
        var resultType = asynchronous
            ? discardResults ? DataType.Task : DataType.TaskOf(new DataType("Results"))
            : discardResults ? DataType.Void : new DataType("Results");
        var method = new Method(methodName, new ReturnType(resultType)).Public
            .AddRootDescription(Summary(discardResults
                ? "Executes the root graph without collecting results."
                : "Executes the root graph and returns its typed results."));
        foreach (var input in inputs)
            method.AddParameter(new Parameter(Type(input.Type), input.Name));
        method.AddParameter(new Parameter(typeof(System.Threading.CancellationToken), "cancellationToken")
            .AddDefault(SimpleNameExpression.Default));
        if (hasLogger)
        {
            var loggerFactoryType = new DataType("global::Microsoft.Extensions.Logging.ILoggerFactory");
            var loggerFactory = Call(
                "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService",
                Name("_services"), Call("typeof", loggerFactoryType.Type)).Cast(loggerFactoryType);
            method.AddStatement(Variable("__loggerFactory", loggerFactory, loggerFactoryType));
            method.AddStatement(Variable("__logger",
                Call(Name("__loggerFactory").Sub("CreateLogger"),
                    (owner.ToDisplayString() + "[" + owner.Name + "]").ToLiteral()),
                new DataType("global::Microsoft.Extensions.Logging.ILogger")));
        }
        var creation = New(Type(owner), inputs.Select(input =>
            (IExpression)Name(input.Name.ToValidIdentifier())).ToArray());
        creation.AddVariable("DisplayName", owner.Name.ToLiteral());
        if (hasLogger)
            creation.AddVariable("Logger", Name("__logger"));
        var arguments = new List<IExpression>();
        if (requiresServices)
            arguments.Add(Name("_services"));
        arguments.Add(Name("cancellationToken"));
        var call = Call(((IExpression)creation).Sub(methodName), arguments.ToArray());
        if (discardResults && !asynchronous) method.AddStatement(call);
        else method.AddStatement(call.Return);
        return method;
    }
}
