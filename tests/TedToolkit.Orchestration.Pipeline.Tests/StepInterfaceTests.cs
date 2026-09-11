using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments("internal class BadOwner { [Step] internal static int Bad(CancellationToken token) => 1; }")]
    [Arguments("internal static class BadOwner { [Step] private static int Bad(CancellationToken token) => 1; }")]
    [Arguments("internal static class BadOwner { [Step] internal static T Bad<T>(CancellationToken token) => default!; }")]
    [Arguments("internal static class Outer { internal static class BadOwner { [Step] internal static int Bad(CancellationToken token) => 1; } }")]
    [Arguments("internal static class BadOwner { [Step] internal static ValueTask<int> Bad(CancellationToken token) => ValueTask.FromResult(1); }")]
    public async Task UnsupportedStepContractsAreRejected(string declaration)
    {
        var generated = await Generate(declaration);
        await Assert.That(generated.Diagnostics.Any(d => d.Id == "TTP013")).IsTrue();
    }

    [Test]
    public async Task FunctionStepsAreDirectlyCallableWithoutGeneratedInstances()
    {
        var generated = await Generate("""
            internal static class Nodes
            {
                [Step]
                internal static Task<int> Node(CancellationToken token) => Task.FromResult(1);
            }
            """ + Scenario("""
                return Nodes.Node(CancellationToken.None)
                    .ContinueWith(value => value.Result.ToString());
                """));

        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource.Contains(
            "partial class Nodes")).IsFalse();
    }

    [Test]
    [Arguments("internal static class BadOwner { [Step] internal static void Bad(Span<int> value, CancellationToken token) {} }")]
    [Arguments("internal static class BadOwner { [Step] internal static void Bad(ref int value, CancellationToken token) {} }")]
    [Arguments("internal static class BadOwner { [Step] internal static void Bad(CancellationToken first, CancellationToken second) {} }")]
    [Arguments("internal static class BadOwner { [Step] internal static void Bad(CancellationToken token, int value) {} }")]
    [Arguments("internal static class BadOwner { [Step] internal static void Bad([FromServices(\"key\")] ILogger logger, CancellationToken token) {} }")]
    public async Task UnsupportedStepParametersAreRejected(string declaration)
    {
        var generated = await Generate(declaration);
        await Assert.That(generated.Diagnostics.Any(d => d.Id == "TTP013")).IsTrue();
    }
}
