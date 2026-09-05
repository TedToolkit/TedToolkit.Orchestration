using System;
using System.Collections.Generic;
using System.Linq;

using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;

using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Owns the generated typed factory surface for one constructible step.
internal sealed class StepFactory
{
    internal StepFactory(INamedTypeSymbol type, IMethodSymbol constructor, ITypeSymbol? result, bool synchronous)
    {
        Type = type;
        Constructor = constructor;
        Result = result;
        IsSynchronous = synchronous;
        var policy = StepSymbols.Policy(type);
        RetryCount = policy.RetryCount;
        TimeoutMilliseconds = policy.TimeoutMilliseconds;
        Parameters = constructor.Parameters.Where(p => !StepSymbols.IsService(p)).ToArray();
    }

    internal INamedTypeSymbol Type { get; }
    internal IMethodSymbol Constructor { get; }
    internal ITypeSymbol? Result { get; }
    internal bool IsSynchronous { get; }
    internal int RetryCount { get; }
    internal int TimeoutMilliseconds { get; }
    internal IParameterSymbol[] Parameters { get; }
    internal Method CreateMethod(Compilation compilation)
    {
        var handle = Result is null ? GeneratedCode.Type(compilation.GetTypeByMetadataName(StepSymbols.VoidBuilderName)!) : Runtime(compilation, "StepBuilder", Result);
        var method = new Method(Type.Name, new ReturnType(handle)).Public.AddRootDescription(Summary("Declares a step for the generator; omitted arguments become execution parameters."));
        foreach (var parameter in Parameters)
        {
            var argument = new Parameter(Runtime(compilation, "StepArgument", parameter.Type), parameter.Name);
            method.AddParameter(argument.AddDefault(SimpleNameExpression.Default));
        }
        return method;
    }
    internal StepFactory Rebind(Compilation compilation)
    {
        var type = compilation.GetTypeByMetadataName(Type.ToDisplayString())!;
        var constructor = type.InstanceConstructors.Single(candidate =>
            candidate.Parameters.Length == Constructor.Parameters.Length &&
            candidate.IsImplicitlyDeclared == Constructor.IsImplicitlyDeclared);
        return new StepFactory(type, constructor, StepSymbols.ResultType(type, compilation), IsSynchronous);
    }

    internal string ExtensionMetadataName => "TedToolkit.Orchestration.Pipeline." + Type.Name + "Extensions";

    internal INamedTypeSymbol? ExistingExtensions => Type.ContainingAssembly.GetTypeByMetadataName(ExtensionMetadataName);

    internal static string EmitExtensions(Compilation compilation, IReadOnlyList<StepFactory> factories)
    {
        var declarations = new List<IMember>();
        var builderType = GeneratedCode.Type(compilation.GetTypeByMetadataName("TedToolkit.Orchestration.Pipeline.Pipeline+Builder")!);
        foreach (var factory in factories)
        {
            if (!SymbolEqualityComparer.Default.Equals(factory.Type.ContainingAssembly, compilation.Assembly) && factory.ExistingExtensions is not null) continue;
            var extensions = new TypeDeclaration(factory.Type.Name + "Extensions", TypeDeclarationType.CLASS).Internal.Static;
            var receiver = "__builder";
            while (factory.Parameters.Any(parameter => parameter.Name == receiver)) receiver += "_";
            var method = factory.CreateMethod(compilation).Internal.Static;
            method.Parameters.Insert(0, new Parameter(builderType, receiver).This);
            method.AddStatement(SimpleNameExpression.Default.Return);
            extensions.AddMember(method);
            declarations.Add(extensions);
        }
        return Render("TedToolkit.Orchestration.Pipeline", declarations.ToArray());
    }

    internal static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol space)
                foreach (var type in Types(space)) yield return type;
            else if (member is INamedTypeSymbol type)
                yield return type;
        }
    }
}

