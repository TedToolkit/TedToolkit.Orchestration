namespace TedToolkit.Orchestration.Pipeline.Playground;

/// <summary>Adds two operands synchronously, without allocating a Step object or result task.</summary>
internal static class AddStepMethods
{
    /// <summary>Adds the operands.</summary>
    [Attributes.Step]
    internal static int AddStep(int a, int b, CancellationToken cancellationToken) => a + b;
}

