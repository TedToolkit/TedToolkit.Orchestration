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
    private readonly IReadOnlyDictionary<IParameterSymbol, string> _inputs;
    private readonly Dictionary<ILocalSymbol, GraphNode> _locals = new(SymbolEqualityComparer.Default);
    private readonly List<GraphNode> _nodes = new();
    private readonly Dictionary<ILocalSymbol, VariableDeclaratorSyntax> _values = new(SymbolEqualityComparer.Default);
    private bool _failed;
    internal GraphReader(SourceProductionContext context, Compilation compilation, MethodDeclarationSyntax configure,
        IReadOnlyList<StepFactory> factories,
        IReadOnlyDictionary<IParameterSymbol, string>? inputs = null)
    {
        _context = context;
        _model = compilation.GetSemanticModel(configure.SyntaxTree);
        _configure = configure;
        _factories = factories;
        _inputs = inputs ?? new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
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
            if (expression is InvocationExpressionSyntax call &&
                TryReadRegistration(call, parameter.Type, seen, out var invocation, out var modifiers))
            {
                if (invocation.Arguments.FirstOrDefault()?.Value is not IParameterReferenceOperation receiver || !SymbolEqualityComparer.Default.Equals(receiver.Parameter, parameter))
                { Fail(call, "use the configuration builder parameter directly"); continue; }
                var factory = _factories.FirstOrDefault(item =>
                    SymbolEqualityComparer.Default.Equals(
                        invocation.TargetMethod.ContainingType, item.ExtensionsIn(_model.Compilation)));
                if (factory is null) { Fail(call, "unknown step factory"); continue; }
                var variable = (statement as LocalDeclarationStatementSyntax)?.Declaration.Variables[0];
                var name = variable?.Identifier.ValueText ?? factory.Type.Name + _nodes.Count;
                var displayName = variable?.Identifier.ValueText ?? factory.Type.Name + "#" + (_nodes.Count + 1);
                var node = new GraphNode(factory)
                    { Index = _nodes.Count, Name = name, DisplayName = displayName };
                foreach (var argument in invocation.Arguments.Where(item => item.Parameter!.Ordinal != 0).OrderBy(item => item.Parameter!.Ordinal))
                {
                    var binding = ReadBinding(argument, factory.Parameters[argument.Parameter!.Ordinal - 1]);
                    node.Arguments.Add(binding);
                }
                ApplyModifiers(node, modifiers);
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
        var mirror = new ExpressionMirror(_model, _configure, _values, _inputs);
        foreach (var node in _nodes) mirror.Apply(node);
        return _nodes;
    }

    private bool TryReadRegistration(InvocationExpressionSyntax call, ITypeSymbol builder,
        HashSet<InvocationExpressionSyntax> seen, out IInvocationOperation invocation,
        out IReadOnlyList<IInvocationOperation> modifiers)
    {
        var current = _model.GetOperation(call) as IInvocationOperation;
        var chain = new List<IInvocationOperation>();
        while (current is not null && PipelineSymbols.IsStepModifier(current.TargetMethod, _model.Compilation))
        {
            chain.Add(current);
            current = Unwrap(current.Instance) as IInvocationOperation;
        }
        if (current is null || !IsFactory(current, builder))
        {
            invocation = null!;
            modifiers = chain;
            return false;
        }
        invocation = current;
        modifiers = chain;
        if (current.Syntax is InvocationExpressionSyntax factory) seen.Add(factory);
        foreach (var item in chain)
            if (item.Syntax is InvocationExpressionSyntax modifier) seen.Add(modifier);
        return true;
    }

    private void ApplyModifiers(GraphNode node, IReadOnlyList<IInvocationOperation> modifiers)
    {
        var configured = new HashSet<string>(StringComparer.Ordinal);
        foreach (var modifier in modifiers.Reverse())
        {
            var name = modifier.TargetMethod.Name;
            if (name != "DependsOn" && !configured.Add(name))
            {
                Fail(modifier.Syntax, name + " can be specified only once per node");
                continue;
            }
            switch (modifier.TargetMethod.Name)
            {
                case "WithRetry":
                    ApplyRetry(node, modifier);
                    break;
                case "WithTimeout":
                    ApplyTimeout(node, modifier);
                    break;
                case "WithDisplayName":
                    ApplyDisplayName(node, modifier);
                    break;
                case "DependsOn":
                    ApplyDependency(node, modifier);
                    break;
            }
        }
    }

    private void ApplyRetry(GraphNode node, IInvocationOperation modifier)
    {
        if (!TryConstant(modifier, out int retries) || retries < 0)
            Fail(modifier.Syntax, "WithRetry requires a nonnegative compile-time constant");
        else node.RetryCount = retries;
    }

    private void ApplyTimeout(GraphNode node, IInvocationOperation modifier)
    {
        if (!TryConstant(modifier, out int milliseconds) || milliseconds < -1 || milliseconds == 0)
            Fail(modifier.Syntax, "WithTimeout requires a compile-time constant of -1 or a positive millisecond value");
        else node.TimeoutMilliseconds = milliseconds;
    }

    private void ApplyDisplayName(GraphNode node, IInvocationOperation modifier)
    {
        if (!TryConstant(modifier, out string? name) || string.IsNullOrEmpty(name))
            Fail(modifier.Syntax, "WithDisplayName requires a nonempty compile-time constant");
        else node.DisplayName = name!;
    }

    private void ApplyDependency(GraphNode node, IInvocationOperation modifier)
    {
        var dependency = Unwrap(modifier.Arguments.Single().Value) as ILocalReferenceOperation;
        if (dependency is null || !_locals.TryGetValue(dependency.Local, out var source))
        {
            Fail(modifier.Syntax, "DependsOn requires a previously declared node handle");
            return;
        }
        if (!node.Arguments.Any(argument => ReferenceEquals(argument.Source, source)) &&
            !node.ControlDependencies.Contains(source))
            node.ControlDependencies.Add(source);
    }

    private static bool TryConstant<T>(IInvocationOperation modifier, out T value)
    {
        var constant = modifier.Arguments.Single().Value.ConstantValue;
        if (constant.HasValue && constant.Value is T item)
        {
            value = item;
            return true;
        }
        value = default!;
        return false;
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation conversion) operation = conversion.Operand;
        return operation;
    }

    private void ValidateUsage(IParameterSymbol parameter, HashSet<InvocationExpressionSyntax> seen)
    {
        foreach (var call in _configure.Body!.DescendantNodes().OfType<InvocationExpressionSyntax>())
            if (_model.GetOperation(call) is IInvocationOperation invocation && !seen.Contains(call))
            {
                if (IsFactory(invocation, parameter.Type))
                    Fail(call, "register each node in a separate unconditional statement");
                else if (PipelineSymbols.IsStepModifier(invocation.TargetMethod, _model.Compilation))
                    Fail(call, "chain node modifiers directly from one generated step registration");
            }
        foreach (var exit in _configure.Body!.DescendantNodes().Where(item => item is ReturnStatementSyntax or GotoStatementSyntax))
            if (!exit.Ancestors().TakeWhile(item => item != _configure).Any(item => item is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                Fail(exit, "configuration cannot return early or jump between registrations");
        foreach (var identifier in _configure.Body!.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbol = _model.GetSymbolInfo(identifier).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, parameter))
            {
                var extensionReceiver = identifier.Parent is MemberAccessExpressionSyntax member &&
                    member.Expression == identifier && member.Parent is InvocationExpressionSyntax extensionCall &&
                    seen.Contains(extensionCall);
                var staticReceiver = identifier.Parent is ArgumentSyntax argument &&
                    argument.Parent is ArgumentListSyntax arguments && arguments.Arguments.FirstOrDefault() == argument &&
                    arguments.Parent is InvocationExpressionSyntax staticCall && seen.Contains(staticCall);
                if (!extensionReceiver && !staticReceiver)
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
        var resultNames = new HashSet<string>(StringComparer.Ordinal) { "Results" };
        foreach (var node in _nodes)
            if (node.Factory.Result is not null && !resultNames.Add(node.ResultName)) Fail(_configure, "result names collide; give the nodes distinct names");
    }

    private static bool IsFactory(IInvocationOperation invocation, ITypeSymbol builder) =>
        invocation.TargetMethod.IsExtensionMethod &&
        SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.Parameters.FirstOrDefault()?.Type, builder);

    private NodeArgument ReadBinding(IArgumentOperation argument, IParameterSymbol parameter)
    {
        var value = argument.Value;
        while (value is IConversionOperation conversion) value = conversion.Operand;
        if (argument.ArgumentKind == ArgumentKind.DefaultValue || value is IDefaultValueOperation && SymbolEqualityComparer.Default.Equals(value.Type, argument.Parameter!.Type))
        {
            Fail(argument.Syntax, "Composite Step registrations must bind every data input");
            return new NodeArgument();
        }
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
            Fail(argument.Syntax, "bindings must be a fixed value or a previously declared node");
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
}
internal sealed class GraphNode
{
    internal GraphNode(StepFactory factory)
    {
        Factory = factory;
        RetryCount = 0;
        TimeoutMilliseconds = -1;
    }
    internal StepFactory Factory { get; }
    internal int Index { get; set; }
    internal string Name { get; set; } = "";
    internal string DisplayName { get; set; } = "";
    internal int RetryCount { get; set; }
    internal int TimeoutMilliseconds { get; set; }
    internal string ResultName => GraphReader.Capitalize(Name);
    internal List<NodeArgument> Arguments { get; } = new();
    internal List<GraphNode> ControlDependencies { get; } = new();
    internal List<LocalValue> Locals { get; } = new();
}
internal sealed class LocalValue
{
    internal int Order { get; set; }
    internal string Name { get; set; } = "";
    internal ITypeSymbol? Type { get; set; }
    internal TedToolkit.RoslynHelper.IExpression Expression { get; set; } = null!;
}



