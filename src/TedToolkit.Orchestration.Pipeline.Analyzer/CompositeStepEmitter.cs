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

        var state = State(configuration, runtimeInputs, requiresServices, isPublic);
        declaration.AddMember(state);
        foreach (var node in nodes)
            declaration.AddMember(StepExecutionEmitter.CreateRun(node,
                ExecutionPlan.IsParallel(nodes), true, runtimeInputs, hierarchicalDisplayPath: true));
        declaration.AddMember(ResultsEmitter.Create(nodes, isPublic));
        declaration.AddMember(ExecutionEmitter.Create(nodes, false, true,
            runtimeInputs, false, requiresServices, hierarchicalDisplayPath: true));
        declaration.AddMember(ExecutionEmitter.Create(nodes, true, true,
            runtimeInputs, false, requiresServices, hierarchicalDisplayPath: true));
        declaration.AddMember(Prepare(configuration, runtimeInputs, requiresServices, isPublic));
        declaration.AddMember(Execute(configuration, runtimeInputs,
            asynchronous, requiresServices, isPublic));
        if (emitPipeline)
            declaration.AddMember(PipelineFacadeEmitter.CompositeFacade(
                configuration, asynchronous, requiresServices));

        var nameSpace = owner.ContainingNamespace.IsGlobalNamespace
            ? "" : GeneratedNames.Namespace(owner.ContainingNamespace);
        return StepFactory.AliasDirectives(nodes.Select(node => node.Factory)) +
            RenderConfiguration(compilation, nameSpace, syntax, declaration);
    }

    private static TypeDeclaration State(IMethodSymbol configuration,
        IReadOnlyList<IParameterSymbol> runtimeInputs, bool requiresServices, bool isPublic)
    {
        var state = new TypeDeclaration(CompositeStepGenerator.StateProtocolName,
            TypeDeclarationType.STRUCT).Readonly
            .AddRootDescription(Summary("Carries prepared state for generated Composite Step execution."));
        _ = isPublic ? state.Public : state.Internal;
        state.AddAttribute(EditorBrowsableAttribute());
        if (requiresServices)
            state.AddMember(new Field(typeof(IServiceProvider), "__services").Internal.Readonly);
        state.AddMember(new Field(typeof(string), "__displayPath").Internal.Readonly);
        for (var index = 0; index < runtimeInputs.Count; index++)
            if (StepSymbols.IsService(runtimeInputs[index]))
                state.AddMember(new Field(Type(runtimeInputs[index].Type), "__root" + index).Internal.Readonly);

        var constructor = new Constructor().Internal;
        if (requiresServices)
            constructor.AddParameter(new Parameter(typeof(IServiceProvider), "services"))
                .AddStatement(Name("__services").Assign(Name("services")));
        constructor.AddParameter(new Parameter(typeof(string), "displayPath"))
            .AddStatement(Name("__displayPath").Assign(Name("displayPath")));
        for (var index = 0; index < runtimeInputs.Count; index++)
        {
            var parameter = runtimeInputs[index];
            if (!StepSymbols.IsService(parameter)) continue;
            constructor.AddParameter(new Parameter(Type(parameter.Type), "root" + index))
                .AddStatement(Name("__root" + index).Assign(Name("root" + index)));
        }
        state.AddMember(constructor);
        return state;
    }

    private static Method Prepare(IMethodSymbol configuration,
        IReadOnlyList<IParameterSymbol> runtimeInputs, bool requiresServices, bool isPublic)
    {
        var method = new Method(CompositeStepGenerator.PrepareProtocolName,
            new ReturnType(new DataType(CompositeStepGenerator.StateProtocolName))).Static
            .AddRootDescription(Summary("Prepares services and display state once for a Composite Step invocation."));
        _ = isPublic ? method.Public : method.Internal;
        method.AddAttribute(EditorBrowsableAttribute());
        var names = new GeneratedNameAllocator(configuration.Parameters.Skip(1)
            .Select(parameter => parameter.Name));
        var servicesName = requiresServices ? names.Allocate("__services") : null;
        var displayPathName = names.Allocate("__displayPath");
        if (requiresServices)
            method.AddParameter(new Parameter(typeof(IServiceProvider), servicesName!));
        method.AddParameter(new Parameter(typeof(string), displayPathName));
        if (requiresServices)
            method.AddStatement(Call(
                "global::TedToolkit.Orchestration.Pipeline.CompilerServices.PipelineExecutionSupport.ThrowIfNull",
                Name(servicesName!), "services".ToLiteral()));

        var serviceValues = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
        for (var index = 0; index < runtimeInputs.Count; index++)
        {
            var parameter = runtimeInputs[index];
            if (!StepSymbols.IsService(parameter) || IsSpecialLogger(parameter)) continue;
            var variable = names.Allocate("__root" + index);
            method.AddStatement(Variable(variable, ResolveService(parameter, servicesName!), Type(parameter.Type)));
            serviceValues.Add(parameter, variable);
        }

        var loggers = runtimeInputs.Where(IsSpecialLogger).ToArray();
        if (loggers.Length != 0)
        {
            var loggerFactoryType = new DataType("global::Microsoft.Extensions.Logging.ILoggerFactory");
            var loggerFactoryName = names.Allocate("__loggerFactory");
            var loggerName = names.Allocate("__logger");
            method.AddStatement(Variable(loggerFactoryName,
                Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService",
                    Name(servicesName!), Call("typeof", loggerFactoryType.Type)).Cast(loggerFactoryType),
                loggerFactoryType));
            var prefix = (configuration.ContainingType.ToDisplayString() + ".Configuration[").ToLiteral();
            method.AddStatement(Variable(loggerName,
                Call(Name(loggerFactoryName).Sub("CreateLogger"),
                    prefix.Add(Name(displayPathName)).Add("]".ToLiteral())),
                new DataType("global::Microsoft.Extensions.Logging.ILogger")));
            foreach (var logger in loggers)
                serviceValues.Add(logger, loggerName);
        }

        var arguments = new List<IExpression>();
        if (requiresServices) arguments.Add(Name(servicesName!));
        arguments.Add(Name(displayPathName));
        arguments.AddRange(runtimeInputs.Where(StepSymbols.IsService)
            .Select(parameter => (IExpression)Name(serviceValues[parameter])));
        method.AddStatement(New(new DataType(CompositeStepGenerator.StateProtocolName),
            arguments.ToArray()).Return);
        return method;
    }

    private static Method Execute(IMethodSymbol configuration,
        IReadOnlyList<IParameterSymbol> runtimeInputs, bool asynchronous,
        bool requiresServices, bool isPublic)
    {
        var method = new Method(CompositeStepGenerator.ExecuteProtocolName,
            new ReturnType(asynchronous ? DataType.TaskOf(new DataType("Results")) : new DataType("Results"))).Static
            .AddRootDescription(Summary("Executes one prepared Composite Step attempt."));
        _ = isPublic ? method.Public : method.Internal;
        var protocol = new TedToolkit.RoslynHelper.Syntaxes.Attribute(new DataType(
            "global::TedToolkit.Orchestration.Pipeline.CompilerServices.GeneratedCompositeStepAttribute"));
        protocol.Arguments.Add(new Argument(1.ToLiteral()));
        protocol.Arguments.Add(new Argument(requiresServices.ToLiteral()));
        method.AddAttribute(protocol);
        method.AddAttribute(EditorBrowsableAttribute());

        var data = runtimeInputs.Where(parameter => !StepSymbols.IsService(parameter)).ToArray();
        var declaredToken = configuration.Parameters.FirstOrDefault(StepSymbols.IsCancellationToken);
        var names = new GeneratedNameAllocator(data.Select(parameter => parameter.Name)
            .Concat(declaredToken is null ? Array.Empty<string>() : new[] { declaredToken.Name }));
        var stateName = names.Allocate("__state");
        method.AddParameter(new Parameter(new DataType(CompositeStepGenerator.StateProtocolName), stateName));
        foreach (var parameter in data)
        {
            var projected = new Parameter(Type(parameter.Type), parameter.Name);
            if (parameter.HasExplicitDefaultValue) projected.AddDefault(ExplicitDefault(parameter));
            method.AddParameter(projected);
        }
        var tokenName = declaredToken?.Name ?? names.Allocate("cancellationToken");
        method.AddParameter(new Parameter(typeof(System.Threading.CancellationToken), tokenName)
            .AddDefault(SimpleNameExpression.Default));

        var arguments = new List<IExpression>();
        if (requiresServices) arguments.Add(Name(stateName).Sub("__services"));
        for (var index = 0; index < runtimeInputs.Count; index++)
        {
            var parameter = runtimeInputs[index];
            arguments.Add(StepSymbols.IsService(parameter)
                ? Name(stateName).Sub("__root" + index)
                : Name(parameter.Name));
        }
        arguments.Add(Name(stateName).Sub("__displayPath"));
        arguments.Add(Name(tokenName));
        method.AddStatement(Call((asynchronous ? "ExecuteAsync" : "Execute") + "Core",
            arguments.ToArray()).Return);
        return method;
    }

    private static bool IsSpecialLogger(IParameterSymbol parameter)
    {
        if (!StepSymbols.IsService(parameter) ||
            StepSymbols.ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value is string)
            return false;
        return parameter.Type.ToDisplayString() == "Microsoft.Extensions.Logging.ILogger";
    }

    private static IExpression ResolveService(IParameterSymbol parameter, string servicesName)
    {
        var runtimeType = ServiceLookupType(parameter.Type);
        var key = StepSymbols.ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value as string;
        return key is null
            ? Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService",
                Name(servicesName), Call("typeof", runtimeType.Type)).Cast(Type(parameter.Type))
            : Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService",
                Name(servicesName), Call("typeof", runtimeType.Type), key.ToLiteral()).Cast(Type(parameter.Type));
    }

    private static TedToolkit.RoslynHelper.Syntaxes.Attribute EditorBrowsableAttribute()
    {
        var attribute = new TedToolkit.RoslynHelper.Syntaxes.Attribute(new DataType(
            "global::System.ComponentModel.EditorBrowsableAttribute"));
        attribute.Arguments.Add(new Argument(Name(
            "global::System.ComponentModel.EditorBrowsableState.Never")));
        return attribute;
    }
}
