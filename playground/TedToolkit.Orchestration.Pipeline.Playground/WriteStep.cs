namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Writes a result without returning a value.</summary>
internal readonly ref struct WriteStep(string message) : IStep
{
    /// <inheritdoc />
    public void Execute(CancellationToken cancellationToken = default) => Console.WriteLine($"Completed: {message}");
}

