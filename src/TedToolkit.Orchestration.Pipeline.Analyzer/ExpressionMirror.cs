using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static TedToolkit.Orchestration.Pipeline.Analyzer.GeneratedCode;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Mirror expressions in their original semantic context, renaming configuration-only locals and inputs.
internal sealed class ExpressionMirror : CSharpSyntaxRewriter
{
    private readonly SemanticModel _model;
    private readonly IMethodSymbol _configuration;
    private readonly IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> _values;
    private readonly IReadOnlyDictionary<IParameterSymbol, string> _inputs;
    private readonly Dictionary<ILocalSymbol, string> _names = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ILocalSymbol> _visited = new(SymbolEqualityComparer.Default);
    private GraphNode _node = null!;

    internal ExpressionMirror(SemanticModel model, MethodDeclarationSyntax configuration,
        IReadOnlyDictionary<ILocalSymbol, VariableDeclaratorSyntax> values,
        IReadOnlyDictionary<IParameterSymbol, string>? inputs = null)
    {
        _model = model;
        _configuration = (IMethodSymbol)model.GetDeclaredSymbol(configuration)!;
        _values = values;
        _inputs = inputs ?? new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var local in values.OrderBy(item => item.Value.SpanStart)) _names.Add(local.Key, "__local" + _names.Count);
    }

    internal void Apply(GraphNode node)
    {
        _node = node;
        _visited.Clear();
        foreach (var binding in node.Arguments.Where(argument => argument.Syntax is not null).OrderBy(argument => argument.Syntax!.SpanStart))
            binding.Expression = Name(Visit(binding.Syntax!)!.WithoutTrivia().NormalizeWhitespace().ToFullString());
        node.Locals.Sort((left, right) => left.Order.CompareTo(right.Order));
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } && _model.GetConstantValue(node) is { HasValue: true, Value: string name })
            return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name));
        if (_model.GetSymbolInfo(node).Symbol is IMethodSymbol { ReducedFrom: { } extension } && node.Expression is MemberAccessExpressionSyntax member)
        {
            var methodName = member.Name is GenericNameSyntax generic ? Visit(generic)!.ToString() : member.Name.ToString();
            var target = SyntaxFactory.ParseExpression(extension.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + methodName);
            var receiver = SyntaxFactory.Argument((ExpressionSyntax)Visit(member.Expression)!);
            return SyntaxFactory.InvocationExpression(target, SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                new[] { receiver }.Concat(node.ArgumentList.Arguments.Select(argument => (ArgumentSyntax)Visit(argument)!)))));
        }
        return base.VisitInvocationExpression(node);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => RewriteName(node) ?? base.VisitIdentifierName(node);
    public override SyntaxNode? VisitGenericName(GenericNameSyntax node) => RewriteName(node) ?? base.VisitGenericName(node);

    private SyntaxNode? RewriteName(SimpleNameSyntax node)
    {
        if (_model.GetSymbolInfo(node).Symbol is IParameterSymbol rootParameter &&
            _inputs.TryGetValue(rootParameter, out var inputName))
            return SyntaxFactory.IdentifierName(inputName);
        if (node.Parent is MemberAccessExpressionSyntax member && member.Name == node ||
            node.Parent is MemberBindingExpressionSyntax || node.Parent is QualifiedNameSyntax qualified && qualified.Right == node ||
            node.Parent is AliasQualifiedNameSyntax || node.Parent is NameColonSyntax or NameEqualsSyntax ||
            node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node && assignment.Parent is InitializerExpressionSyntax)
            return null;
        var symbol = _model.GetSymbolInfo(node).Symbol;
        if (symbol is IParameterSymbol parameter && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, _configuration))
            return SyntaxFactory.ParseExpression("this._configuration" + (parameter.Ordinal - 1));
        if (symbol is ILocalSymbol local && _values.TryGetValue(local, out var declaration))
        {
            if (_visited.Add(local))
            {
                var expression = Name(Visit(declaration.Initializer!.Value)!.WithoutTrivia().NormalizeWhitespace().ToFullString());
                _node.Locals.Add(new LocalValue { Order = declaration.SpanStart, Name = _names[local], Type = local.Type.IsAnonymousType ? null : local.Type, Expression = expression });
            }
            return SyntaxFactory.IdentifierName(_names[local]);
        }
        if (symbol is INamedTypeSymbol type)
            return SyntaxFactory.ParseName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        if (symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol && symbol.ContainingType is { } owner &&
            symbol is not IMethodSymbol { MethodKind: MethodKind.LocalFunction })
        {
            var prefix = symbol.IsStatic ? owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "this";
            var name = node is GenericNameSyntax generic
                ? generic.WithTypeArgumentList((TypeArgumentListSyntax)Visit(generic.TypeArgumentList)!).ToString()
                : node.ToString();
            return SyntaxFactory.ParseExpression(prefix + "." + name);
        }
        return null;
    }

    public override SyntaxNode? VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
    {
        var rewritten = (AnonymousObjectMemberDeclaratorSyntax)base.VisitAnonymousObjectMemberDeclarator(node)!;
        if (node.NameEquals is null && node.Expression is IdentifierNameSyntax name && rewritten.Expression.ToString() != name.ToString())
            rewritten = rewritten.WithNameEquals(SyntaxFactory.NameEquals(name));
        return rewritten;
    }

    public override SyntaxNode? VisitArgument(ArgumentSyntax node)
    {
        var rewritten = (ArgumentSyntax)base.VisitArgument(node)!;
        if (node.Parent is TupleExpressionSyntax && node.NameColon is null && node.Expression is IdentifierNameSyntax name && rewritten.Expression.ToString() != name.ToString())
            rewritten = rewritten.WithNameColon(SyntaxFactory.NameColon(name));
        return rewritten;
    }
}

