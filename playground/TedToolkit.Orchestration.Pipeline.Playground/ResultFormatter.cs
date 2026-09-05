namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Formats a labeled result.</summary>
public interface IResultFormatter
{
    /// <summary>Formats the label and calculated value.</summary>
    /// <param name="label">The result label.</param>
    /// <param name="value">The calculated value.</param>
    /// <returns>The formatted result.</returns>
    string Format(string label, int value);
}

/// <summary>The Playground's DI service implementation.</summary>
public sealed class ResultFormatter : IResultFormatter
{
    /// <inheritdoc />
    public string Format(string label, int value) => $"{label}: {value}";
}

