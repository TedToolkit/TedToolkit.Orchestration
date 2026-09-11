using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;

using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Shared naming, runtime types and file settings for generated pipeline code.
internal static class GeneratedCode
{
    internal static DescriptionSummary Summary(string text) => new(new DescriptionText(text));
    internal static SimpleNameExpression Name(string name) => name.ToSimpleName();
    internal static DataType Type(ITypeSymbol symbol) => new(StepSymbols.TypeName(symbol));
    internal static DataType Runtime(Compilation compilation, string name, params ITypeSymbol[] arguments)
    {
        var type = compilation.GetTypeByMetadataName("TedToolkit.Orchestration.Pipeline." + name + (arguments.Length == 0 ? "" : "`" + arguments.Length))!;
        return Type(arguments.Length == 0 ? type : type.Construct(arguments));
    }
    internal static DataType TaskOf(ITypeSymbol? result) => result is null ? DataType.Task : DataType.TaskOf(Type(result));
    internal static DataType ServiceLookupType(
        ITypeSymbol type, Func<ITypeSymbol, DataType>? render = null) =>
        type.TypeKind == TypeKind.Dynamic
            ? DataType.FromType(typeof(object))
            : (render ?? Type)(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
    internal static InvocationExpression Call(string name, params IExpression[] arguments) => Name(name).Invoke(arguments);
    internal static InvocationExpression Call(IExpression target, params IExpression[] arguments) => target.Invoke(arguments);
    internal static ObjectCreationExpression New(DataType type, params IExpression[] arguments) => type.New.AddArguments(arguments);
    internal static IExpression Await(IExpression expression) => expression.ConfigureAwait(false).Await();
    internal static VariableExpression Variable(string name, IExpression value, DataType? type = null) =>
        new VariableExpression(type ?? DataType.Var, name).AddDefault(value);

    internal static IExpression ExplicitDefault(
        IParameterSymbol parameter, Func<ITypeSymbol, string>? typeName = null)
    {
        if (!parameter.HasExplicitDefaultValue || parameter.ExplicitDefaultValue is null)
            return SimpleNameExpression.Default;
        var value = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(
            parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false)!;
        if (parameter.Type.TypeKind == TypeKind.Enum)
            return Name("(" + (typeName ?? StepSymbols.TypeName)(parameter.Type) + ")" + value);
        return Name(value);
    }

    internal static string Render(string nameSpace, params IMember[] members)
    {
        var file = CreateFile();
        AddMembers(file, nameSpace, members);
        return file.ToCode();
    }

    internal static string RenderConfiguration(Compilation compilation, string nameSpace, MethodDeclarationSyntax configuration, params IMember[] members)
    {
        var file = CreateFile();
        var root = (CompilationUnitSyntax)configuration.SyntaxTree.GetRoot();
        var model = compilation.GetSemanticModel(configuration.SyntaxTree);
        var imports = root.Usings.Concat(configuration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().SelectMany(space => space.Usings));
        foreach (var import in imports.Where(import => import.GlobalKeyword.RawKind == 0).GroupBy(import => import.Alias?.Name.ToString() ?? import.ToString()).Select(group => group.Last()))
        {
            var symbol = import.Name is null ? null : model.GetSymbolInfo(import.Name).Symbol;
            var target = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? import.Name?.ToString();
            if (symbol is INamespaceSymbol && target is not null && !target.StartsWith("global::")) target = "global::" + target;
            var text = (import.Alias is null ? "" : import.Alias.Name + " = ") + target;
            file.AddUsing(new UsingDirective(Name(text)) { IsStatic = import.StaticKeyword.RawKind != 0 });
        }
        AddMembers(file, nameSpace, members);
        return file.ToCode();
    }
    private static void AddMembers(SourceFile file, string nameSpace, IMember[] members)
    {
        if (nameSpace.Length == 0)
        {
            file.Members.AddRange(members);
            return;
        }
        var space = SourceComposer.NameSpace(nameSpace);
        space.Members.AddRange(members);
        file.AddNameSpace(space);
    }

    private static SourceFile CreateFile() => new()
    {
        DisableWarnings = false,
        NullableContext = NullableContextOptions.Enable,
    };
}
