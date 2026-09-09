namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Adds two operands synchronously, without allocating a Step object or result task.</summary>
internal readonly ref partial struct AddStep(int a, int b) : IStep<int>
{
    /// <inheritdoc />
    public int Execute(CancellationToken cancellationToken = default) => a + b;
}

