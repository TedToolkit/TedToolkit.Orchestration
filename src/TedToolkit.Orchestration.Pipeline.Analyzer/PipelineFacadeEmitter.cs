using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;
using Accessibility = Microsoft.CodeAnalysis.Accessibility;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal static class PipelineFacadeEmitter
{
    internal static string? ContractError(IMethodSymbol method)
    {
        if (!method.ContainingType.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is ClassDeclarationSyntax declaration &&
            declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            return "the containing static class must be partial";
        var stem = NameStem(method, out var explicitName);
        if (string.IsNullOrWhiteSpace(stem) || !SyntaxFacts.IsValidIdentifier(stem))
            return "Name must be a nonempty valid C# identifier stem";
        if (explicitName && stem.EndsWith("Pipeline", StringComparison.Ordinal))
            return "Name is a stem; omit the Pipeline suffix because the generator appends it";
        var finalName = stem + "Pipeline";
        if (method.ContainingType.GetMembers(finalName).Any(StepSymbols.IsUserAuthored))
            return "generated type '" + finalName + "' collides with an existing member";
        var duplicates = method.ContainingType.GetMembers().OfType<IMethodSymbol>()
            .Where(candidate => StepSymbols.IsPipeline(candidate))
            .Count(candidate => string.Equals(NameStem(candidate, out _) + "Pipeline",
                finalName, StringComparison.Ordinal));
        return duplicates > 1 ? "multiple Pipeline entries generate '" + finalName + "'" : null;
    }

    internal static string EmitLeaf(Compilation compilation, StepFactory factory,
        MethodDeclarationSyntax declaration)
    {
        var facade = CreateFacade(factory.Callable, factory.RequiresServices);
        facade.AddMember(LeafMethod(factory, discardResults: false));
        facade.AddMember(LeafMethod(factory, discardResults: true));
        return RenderOwner(compilation, factory.Callable, declaration, facade);
    }

    internal static TypeDeclaration CompositeFacade(IMethodSymbol configuration,
        bool asynchronous, bool requiresServices)
    {
        var facade = CreateFacade(configuration, requiresServices);
        facade.AddMember(CompositeMethod(configuration, asynchronous, requiresServices, false));
        facade.AddMember(CompositeMethod(configuration, asynchronous, requiresServices, true));
        return facade;
    }

    private static TypeDeclaration CreateFacade(IMethodSymbol method, bool requiresServices)
    {
        var facade = new TypeDeclaration(NameStem(method, out _) + "Pipeline", TypeDeclarationType.CLASS)
            .Sealed.AddRootDescription(Summary("Provides a reusable root execution facade for this Step."));
        _ = method.ContainingType.DeclaredAccessibility == Accessibility.Public &&
            method.DeclaredAccessibility == Accessibility.Public ? facade.Public : facade.Internal;
        if (requiresServices)
        {
            facade.AddMember(new Field(typeof(IServiceProvider), "_services").Private.Readonly);
            facade.AddMember(new Constructor().Public
                .AddRootDescription(Summary("Creates a root facade using the supplied service provider."))
                .AddParameter(new Parameter(typeof(IServiceProvider), "services"))
                .AddStatement(Call(
                    "global::TedToolkit.Orchestration.Pipeline.CompilerServices.PipelineExecutionSupport.ThrowIfNull",
                    Name("services"), "services".ToLiteral()))
                .AddStatement(Name("_services").Assign(Name("services"))));
        }
        else
            facade.AddMember(new Constructor().Public
                .AddRootDescription(Summary("Creates a root execution facade.")));
        return facade;
    }

    private static Method LeafMethod(StepFactory factory, bool discardResults)
    {
        var asynchronous = !factory.IsSynchronous;
        var returnType = asynchronous
            ? discardResults || factory.Result is null ? DataType.Task : TaskOf(factory.Result)
            : discardResults || factory.Result is null ? DataType.Void : Type(factory.Result);
        var method = new Method(
            (discardResults ? "ExecuteWithoutResults" : "Execute") + (asynchronous ? "Async" : ""),
            new ReturnType(returnType)).Public.AddRootDescription(Summary(discardResults
                ? "Executes the root Step without collecting its result."
                : "Executes the root Step and returns its result."));
        method.IsAsync = asynchronous;
        AddDataParameters(method, factory.Parameters);
        var token = factory.Callable.Parameters.Single(StepSymbols.IsCancellationToken);
        method.AddParameter(new Parameter(Type(token.Type), token.Name)
            .AddDefault(SimpleNameExpression.Default));
        method.AddStatement(Call(token.Name + ".ThrowIfCancellationRequested"));

        var names = new GeneratedNameAllocator(
            factory.Callable.Parameters.Select(parameter => parameter.Name));
        var serviceArguments = new Dictionary<IParameterSymbol, IExpression>(
            SymbolEqualityComparer.Default);
        foreach (var parameter in factory.Callable.Parameters)
        {
            if (!StepSymbols.IsService(parameter) ||
                factory.IsSpecialLogger(parameter))
                continue;
            var serviceName = names.Allocate("__service" + parameter.Ordinal);
            var runtimeType = ServiceLookupType(parameter.Type);
            var key = StepSymbols.ServiceAttribute(parameter)?.ConstructorArguments.FirstOrDefault().Value as string;
            method.AddStatement(Variable(serviceName, ResolveService(runtimeType, key), Type(parameter.Type)));
            serviceArguments.Add(parameter, Name(serviceName));
        }

        if (factory.HasLogger)
        {
            var loggerFactoryName = names.Allocate("__loggerFactory");
            var loggerName = names.Allocate("__logger");
            var loggerFactoryType = new DataType("global::Microsoft.Extensions.Logging.ILoggerFactory");
            var loggerFactory = ResolveService(loggerFactoryType, null);
            method.AddStatement(Variable(loggerFactoryName, loggerFactory, loggerFactoryType));
            method.AddStatement(Variable(loggerName,
                Call(Name(loggerFactoryName).Sub("CreateLogger"),
                    (factory.Identity + "[" + factory.Name + "]").ToLiteral()),
                new DataType("global::Microsoft.Extensions.Logging.ILogger")));
            foreach (var logger in factory.SpecialLoggerParameters)
                serviceArguments.Add(logger, Name(loggerName));
        }

        var arguments = new List<IExpression>();
        foreach (var parameter in factory.Callable.Parameters)
        {
            arguments.Add(StepSymbols.IsService(parameter)
                ? serviceArguments[parameter]
                : Name(parameter.Name));
        }

        IExpression call = Call(Name(StepSymbols.TypeName(factory.Type)).Sub(factory.Callable.Name),
            arguments.ToArray());
        if (asynchronous) call = Await(call);
        if (factory.Result is null || discardResults)
            method.AddStatement(call);
        else
        {
            var resultName = names.Allocate("__result");
            method.AddStatement(Variable(resultName, call, Type(factory.Result)));
            method.AddStatement(Call(token.Name + ".ThrowIfCancellationRequested"));
            method.AddStatement(Name(resultName).Return);
            return method;
        }
        method.AddStatement(Call(token.Name + ".ThrowIfCancellationRequested"));
        return method;
    }

    private static Method CompositeMethod(IMethodSymbol configuration, bool asynchronous,
        bool requiresServices, bool discardResults)
    {
        var returnType = asynchronous
            ? discardResults ? DataType.Task : DataType.TaskOf(new DataType("Results"))
            : discardResults ? DataType.Void : new DataType("Results");
        var method = new Method(
            (discardResults ? "ExecuteWithoutResults" : "Execute") + (asynchronous ? "Async" : ""),
            new ReturnType(returnType)).Public.AddRootDescription(Summary(discardResults
                ? "Executes the root graph without collecting results."
                : "Executes the root graph and returns its typed results."));
        var data = configuration.Parameters.Skip(1).Where(parameter =>
            !StepSymbols.IsService(parameter) && !StepSymbols.IsCancellationToken(parameter)).ToArray();
        AddDataParameters(method, data);
        var declaredToken = configuration.Parameters.FirstOrDefault(StepSymbols.IsCancellationToken);
        var names = new GeneratedNameAllocator(data.Select(parameter => parameter.Name)
            .Concat(declaredToken is null ? Array.Empty<string>() : new[] { declaredToken.Name }));
        var tokenName = declaredToken?.Name ?? names.Allocate("cancellationToken");
        method.AddParameter(new Parameter(typeof(System.Threading.CancellationToken), tokenName)
            .AddDefault(SimpleNameExpression.Default));
        var prepareArguments = new List<IExpression>();
        if (requiresServices) prepareArguments.Add(Name("this._services"));
        prepareArguments.Add(configuration.ContainingType.Name.ToLiteral());
        var stateName = names.Allocate("__state");
        method.AddStatement(Variable(stateName, Call(
            Name(StepSymbols.TypeName(configuration.ContainingType)).Sub(CompositeStepGenerator.PrepareProtocolName),
            prepareArguments.ToArray())));
        var executeArguments = new List<IExpression> { Name(stateName) };
        executeArguments.AddRange(data.Select(parameter => (IExpression)Name(parameter.Name)));
        executeArguments.Add(Name(tokenName));
        IExpression call = Call(
            Name(StepSymbols.TypeName(configuration.ContainingType)).Sub(CompositeStepGenerator.ExecuteProtocolName),
            executeArguments.ToArray());
        if (asynchronous || !discardResults) method.AddStatement(call.Return);
        else method.AddStatement(call);
        return method;
    }

    private static void AddDataParameters(Method method, IEnumerable<IParameterSymbol> parameters)
    {
        foreach (var parameter in parameters)
        {
            var generated = new Parameter(Type(parameter.Type), parameter.Name);
            if (parameter.HasExplicitDefaultValue)
                generated.AddDefault(ExplicitDefault(parameter));
            method.AddParameter(generated);
        }
    }

    private static IExpression ResolveService(DataType type, string? key)
    {
        var provider = Name("this._services");
        return key is null
            ? Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService",
                provider, Call("typeof", type.Type)).Cast(type)
            : Call("global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService",
                provider, Call("typeof", type.Type), key.ToLiteral()).Cast(type);
    }

    private static string RenderOwner(Compilation compilation, IMethodSymbol method,
        MethodDeclarationSyntax source, TypeDeclaration member)
    {
        var owner = new TypeDeclaration(method.ContainingType.Name, TypeDeclarationType.CLASS).Static.Partial;
        _ = method.ContainingType.DeclaredAccessibility == Accessibility.Public ? owner.Public : owner.Internal;
        owner.AddMember(member);
        var nameSpace = method.ContainingNamespace.IsGlobalNamespace
            ? "" : GeneratedNames.Namespace(method.ContainingNamespace);
        return RenderConfiguration(compilation, nameSpace, source, owner);
    }

    private static string NameStem(IMethodSymbol method, out bool explicitName)
    {
        var attribute = StepSymbols.PipelineAttribute(method);
        var pair = attribute?.NamedArguments.FirstOrDefault(argument => argument.Key == "Name") ?? default;
        explicitName = pair.Key is not null;
        return explicitName ? pair.Value.Value as string ?? "" : method.Name;
    }

}
