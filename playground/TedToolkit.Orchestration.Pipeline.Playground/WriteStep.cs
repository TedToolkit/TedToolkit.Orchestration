namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Writes a result without returning a value.</summary>
internal static class WriteStepMethods
{
    /// <summary>Writes the supplied message.</summary>
    [Attributes.Step]
    internal static void WriteStep(string message, CancellationToken cancellationToken) =>
        Console.WriteLine($"Completed: {message}");
}

