using TedToolkit.Orchestration.Pipeline.Attributes;

namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Formats a result through a caller-owned service.</summary>
internal static class FormatStepMethods
{
    /// <summary>Formats the supplied result.</summary>
    [Step]
    internal static string FormatStep(
        int sum,
        string label,
        [FromServices] IResultFormatter formatter,
        CancellationToken cancellationToken) => formatter.Format(label, sum);
}

