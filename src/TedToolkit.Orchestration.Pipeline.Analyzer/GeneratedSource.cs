using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

internal sealed class GeneratedSource : IEquatable<GeneratedSource>
{
    internal GeneratedSource(string hintName, string source)
    {
        HintName = hintName;
        Source = source;
    }

    internal string HintName { get; }
    internal string Source { get; }

    public bool Equals(GeneratedSource? other) => other is not null &&
        StringComparer.Ordinal.Equals(HintName, other.HintName) &&
        StringComparer.Ordinal.Equals(Source, other.Source);

    public override bool Equals(object? obj) => Equals(obj as GeneratedSource);

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(HintName) * 397) ^
                StringComparer.Ordinal.GetHashCode(Source);
        }
    }
}

internal sealed class CompositeGenerationResult : IEquatable<CompositeGenerationResult>
{
    internal CompositeGenerationResult(
        ImmutableArray<GeneratedSource> sources,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Sources = sources;
        Diagnostics = diagnostics;
    }

    internal ImmutableArray<GeneratedSource> Sources { get; }
    internal ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool Equals(CompositeGenerationResult? other) => other is not null &&
        Sources.SequenceEqual(other.Sources) &&
        Diagnostics.Length == other.Diagnostics.Length &&
        Diagnostics.Zip(other.Diagnostics, DiagnosticEquals).All(equal => equal);

    public override bool Equals(object? obj) => Equals(obj as CompositeGenerationResult);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var source in Sources) hash = (hash * 31) + source.GetHashCode();
            foreach (var diagnostic in Diagnostics)
            {
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(diagnostic.Id);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(
                    diagnostic.GetMessage(CultureInfo.InvariantCulture));
                hash = (hash * 31) + diagnostic.Location.SourceSpan.GetHashCode();
            }
            return hash;
        }
    }

    private static bool DiagnosticEquals(Diagnostic left, Diagnostic right) =>
        left.Id == right.Id &&
        left.Severity == right.Severity &&
        left.WarningLevel == right.WarningLevel &&
        left.IsSuppressed == right.IsSuppressed &&
        left.GetMessage(CultureInfo.InvariantCulture) ==
            right.GetMessage(CultureInfo.InvariantCulture) &&
        left.Location.SourceSpan == right.Location.SourceSpan &&
        StringComparer.Ordinal.Equals(
            left.Location.SourceTree?.FilePath, right.Location.SourceTree?.FilePath);
}
