using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Configuration is declaration-only; expressions are mirrored into per-step execution methods.
internal sealed class GraphReader
{
    private readonly SourceProductionContext _context;
    private readonly SemanticModel _model;
    private readonly MethodDeclarationSyntax _configure;
    private readonly IReadOnlyList<StepFactory> _factories;
    private readonly Dictionary<ILocalSymbol, GraphNode> _locals = new(SymbolEqualityComparer.Default);
    private readonly List<GraphNode> _nodes = new();
    private readonly Dictionary<ILocalSymbol, VariableDeclaratorSyntax> _values = new(SymbolEqualityComparer.Default);
    private bool _failed;
    internal GraphReader(SourceProductionContext context, Compilation compilation, MethodDeclarationSyntax configure, IReadOnlyList<StepFactory> factories)
    {
        _context = context;
        _model = compilation.GetSemanticModel(configure.SyntaxTree);
        _configure = configure;
        _factories = factories;
    }

    internal IReadOnlyList<GraphNode>? Read()
    {
        var parameter = _model.GetDeclaredSymbol(_configure.ParameterList.Parameters[0])!;
        var seen = new HashSet<InvocationExpressionSyntax>();
        foreach (var statement in _configure.Body!.Statements)
        {
            var expression = statement is LocalDeclarationStatementSyntax local && local.Declaration.Variables.Count == 1
                ? local.Declaration.Variables[0].Initializer?.Value
                : (statement as ExpressionStatementSyntax)?.Expression;
            if (expression is InvocationExpressionSyntax call && _model.GetOperation(call) is IInvocationOperation invocation &&
                IsFactory(invocation, parameter.Type))
            {
                seen.Add(call);
                if (invocation.Arguments.FirstOrDefault()?.Value is not IParameterReferenceOperation receiver || !SymbolEqualityComparer.Default.Equals(receiver.Parameter, parameter))
                { Fail(call, "use the configuration builder parameter directly"); continue; }
                var factory = _factories.FirstOrDefault(item => item.Type.Name == invocation.TargetMethod.Name);
                if (factory is null) { Fail(call, "unknown step factory"); continue; }
                var expectedExtensions = factory.ExistingExtensions ?? _model.Compilation.Assembly.GetTypeByMetadataName(factory.ExtensionMetadataName);
                if (!SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, expectedExtensions))
                { Fail(call, "use the generated step extension method"); continue; }
                var variable = (statement as LocalDeclarationStatementSyntax)?.Declaration.Variables[0];
                var name = variable?.Identifier.ValueText ?? factory.Type.Name + _nodes.Count;
                var node = new GraphNode(factory) { Index = _nodes.Count, Name = name };
                foreach (var argument in invocation.Arguments.Where(item => item.Parameter!.Ordinal != 0).OrderBy(item => item.Parameter!.Ordinal))
                {
                    var binding = ReadBinding(argument, factory.Parameters[argument.Parameter!.Ordinal - 1]);
                    node.Arguments.Add(binding);
                }
                _nodes.Add(node);
                if (variable is not null && _model.GetDeclaredSymbol(variable) is ILocalSymbol symbol) _locals.Add(symbol, node);
            }
            else if (statement is LocalDeclarationStatementSyntax alias && alias.Declaration.Variables.Count == 1 &&
                alias.Declaration.Variables[0] is { Initializer.Value: IdentifierNameSyntax identifier } declarator &&
                _model.GetSymbolInfo(identifier).Symbol is ILocalSymbol source && _locals.TryGetValue(source, out var sourceNode) &&
                _model.GetDeclaredSymbol(declarator) is ILocalSymbol target) _locals.Add(target, sourceNode);
            else if (statement is LocalDeclarationStatementSyntax values && values.UsingKeyword.IsKind(SyntaxKind.None) &&
                values.Declaration.Type is not RefTypeSyntax && values.Declaration.Variables.All(item => item.Initializer is not null))
            {
                foreach (var value in values.Declaration.Variables)
                    if (_model.GetDeclaredSymbol(value) is ILocalSymbol symbol) _values.Add(symbol, value);
            }
            else if (statement is not EmptyStatementSyntax)
                Fail(statement, "configuration accepts only step declarations and initialized locals; move executable logic into argument expressions or helper methods");
        }
        ValidateUsage(parameter, seen);
        AssignNames();
        if (_failed) return null;
        var mirror = new ExpressionMirror(_model, _configure, _values);
        foreach (var node in _nodes) mirror.Apply(node);
        return _nodes;
    }

    private void ValidateUsage(IParameterSymbol parameter, HashSet<InvocationExpressionSyntax> seen)
    {
        foreach (var call in _configure.Body!.DescendantNodes().OfType<InvocationExpressionSyntax>())
            if (_model.GetOperation(call) is IInvocationOperation invocation &&
                IsFactory(invocation, parameter.Type) && !seen.Contains(call))
                Fail(call, "register each node in a separate unconditional statement");
        foreach (var exit in _configure.Body!.DescendantNodes().Where(item => item is ReturnStatementSyntax or GotoStatementSyntax))
            if (!exit.Ancestors().TakeWhile(item => item != _configure).Any(item => item is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                Fail(exit, "configuration cannot return early or jump between registrations");
        foreach (var identifier in _configure.Body!.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbol = _model.GetSymbolInfo(identifier).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, parameter))
            {
                if (identifier.Parent is not MemberAccessExpressionSyntax member || member.Expression != identifier ||
                    member.Parent is not InvocationExpressionSyntax call || !seen.Contains(call))
                    Fail(identifier, "the configuration builder cannot escape or be aliased");
            }
            if (symbol is ILocalSymbol local && (_locals.ContainsKey(local) || _values.ContainsKey(local)) && identifier.Ancestors().TakeWhile(item => item is not StatementSyntax).Any(item =>
                item is AssignmentExpressionSyntax assignment && assignment.Left.Span.Contains(identifier.Span) ||
                item is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None) || item is RefExpressionSyntax ||
                item is PrefixUnaryExpressionSyntax prefix && (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)) ||
                item is PostfixUnaryExpressionSyntax postfix && (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression))))
                Fail(identifier, "node handles cannot be reassigned or passed by reference");
        }
    }

    private void AssignNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var resultNames = new HashSet<string>(StringComparer.Ordinal) { "Results" };
        foreach (var node in _nodes)
        {
            if (node.Factory.Result is not null && !resultNames.Add(node.ResultName)) Fail(_configure, "result names collide; give the nodes distinct names");
            for (var i = 0; i < node.Arguments.Count; i++)
                if (node.Arguments[i].Unbound)
                {
                    var name = char.ToLowerInvariant(node.Name[0]) + node.Name.Substring(1) + Capitalize(node.Factory.Parameters[i].Name);
                    if (!names.Add(name) || name == "cancellationToken" || ReservedNames.Contains(name)) Fail(_configure, "execution parameter names collide; rename the nodes");
                    node.Arguments[i].InputName = name;
                }
        }
    }

    private static bool IsFactory(IInvocationOperation invocation, ITypeSymbol builder) =>
        invocation.TargetMethod.IsExtensionMethod &&
        SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.Parameters.FirstOrDefault()?.Type, builder);

    private NodeArgument ReadBinding(IArgumentOperation argument, IParameterSymbol parameter)
    {
        var value = argument.Value;
        while (value is IConversionOperation conversion) value = conversion.Operand;
        if (argument.ArgumentKind == ArgumentKind.DefaultValue || value is IDefaultValueOperation && SymbolEqualityComparer.Default.Equals(value.Type, argument.Parameter!.Type)) return new NodeArgument { Unbound = true };
        if (value is ILocalReferenceOperation local && _locals.TryGetValue(local.Local, out var node))
        {
            if (node.Factory.Result is null || !StepSymbols.SameType(node.Factory.Result, parameter.Type))
            {
                _failed = true;
                _context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.ResultTypeMismatch, argument.Syntax.GetLocation(), node.Factory.Result?.ToDisplayString() ?? "void", parameter.Name, parameter.Type.ToDisplayString()));
            }
            return new NodeArgument { Source = node };
        }
        var type = value.Type as INamedTypeSymbol;
        if (type is not null && (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _model.Compilation.GetTypeByMetadataName(StepSymbols.BuilderName)) ||
            SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _model.Compilation.GetTypeByMetadataName(StepSymbols.ArgumentName))))
            Fail(argument.Syntax, "bindings must be a fixed value, an omitted argument, or a previously declared node");
        var expression = argument.Syntax is ArgumentSyntax syntax ? syntax.Expression : (ExpressionSyntax)argument.Value.Syntax;
        while (_model.GetTypeInfo(expression).Type is INamedTypeSymbol expressionType &&
            SymbolEqualityComparer.Default.Equals(expressionType.OriginalDefinition, _model.Compilation.GetTypeByMetadataName(StepSymbols.ArgumentName)))
        {
            if (expression is ParenthesizedExpressionSyntax parentheses) expression = parentheses.Expression;
            else if (expression is CastExpressionSyntax cast) expression = cast.Expression;
            else break;
        }
        return new NodeArgument { Syntax = expression, IsConstant = _model.GetConstantValue(expression).HasValue };
    }
    private static readonly string[] ReservedNames = { "executionToken", "cancellation", "failure" };
    internal static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value.Substring(1);
    private void Fail(SyntaxNode syntax, string reason)
    { _failed = true; _context.ReportDiagnostic(Diagnostic.Create(PipelineDiagnostics.StaticGraph, syntax.GetLocation(), reason)); }
}

internal sealed class NodeArgument
{
    internal bool IsConstant { get; set; }
    internal ExpressionSyntax? Syntax { get; set; }
    internal TedToolkit.RoslynHelper.IExpression? Expression { get; set; }
    internal GraphNode? Source { get; set; }
    internal bool Unbound { get; set; }
    internal string InputName { get; set; } = "";
}
internal sealed class GraphNode
{
    internal GraphNode(StepFactory factory) => Factory = factory;
    internal StepFactory Factory { get; }
    internal int Index { get; set; }
    internal string Name { get; set; } = "";
    internal string ResultName => GraphReader.Capitalize(Name);
    internal List<NodeArgument> Arguments { get; } = new();
    internal List<LocalValue> Locals { get; } = new();
}
internal sealed class LocalValue
{
    internal int Order { get; set; }
    internal string Name { get; set; } = "";
    internal ITypeSymbol? Type { get; set; }
    internal TedToolkit.RoslynHelper.IExpression Expression { get; set; } = null!;
}



