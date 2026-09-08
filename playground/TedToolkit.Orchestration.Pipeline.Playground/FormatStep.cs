using TedToolkit.Orchestration.Pipeline.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Formats a result through a caller-owned service.</summary>
internal readonly ref partial struct FormatStep(int sum, string label, [FromServices] IResultFormatter formatter) : IStep<string>
{
    /// <inheritdoc />
    public string Execute(CancellationToken cancellationToken = default) => formatter.Format(label, sum);
}

