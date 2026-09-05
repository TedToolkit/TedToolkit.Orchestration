using System.Collections.Generic;
using TedToolkit.RoslynHelper;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// RoslynHelper has no standalone scope or while statement. Compose their bodies with its statement API.
internal sealed class GeneratedBlock : IStatement, IStatementOwner
{
    private readonly IExpression? _condition;
    internal GeneratedBlock(IExpression? condition = null) => _condition = condition;
    public List<IStatement> Statements { get; } = new();
    public void ToCode(ref SourceBuilder builder)
    {
        if (_condition is not null)
        {
            builder.Append("while (");
            _condition.ToCode(ref builder);
            builder.Append(')');
        }
        builder.BeginBlock();
        foreach (var statement in Statements) { builder.AppendLine(); statement.ToCode(ref builder); }
        builder.EndBlock();
    }
}
