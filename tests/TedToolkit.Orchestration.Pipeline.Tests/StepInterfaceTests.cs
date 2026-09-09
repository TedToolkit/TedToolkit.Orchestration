using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments("class Bad : IAsyncStep<int> { public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(1); }")]
    [Arguments("struct Bad : IStep<int> { public int Execute(CancellationToken token) => 1; }")]
    [Arguments("ref struct Bad : IAsyncStep<int>, IDisposable { public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(1); public void Dispose() {} }")]
    [Arguments("ref struct Bad : IAsyncStep<int>, IStep<int> { public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(1); public int Execute(CancellationToken token) => 1; }")]
    [Arguments("ref struct Bad : IAsyncStep<int> { Task<int> IAsyncStep<int>.ExecuteAsync(CancellationToken token) => Task.FromResult(1); }")]
    public async Task UnsupportedStepContractsAreRejected(string declaration)
    {
        var generated = await Generate("internal " + declaration);
        await Assert.That(generated.Diagnostics.Any(d => d.Id == "TTP013")).IsTrue();
    }

    [Test]
    public async Task RefStructStepsCannotBeBoxedAsTheirInterface()
    {
        var generated = await Generate("""
            internal readonly ref partial struct Node : IAsyncStep<int> { public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(1); }
            """ + Scenario("IAsyncStep<int> boxed = new Node(); return Task.FromResult(string.Empty);"));
        await Assert.That(generated.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS"))).IsTrue();
    }

    [Test]
    [Arguments("internal readonly ref partial struct Bad(Span<int> value) : IStep { public void Execute(CancellationToken token) {} }")]
    [Arguments("internal readonly ref partial struct Bad : IStep { public Bad(int value) {} public Bad() {} public void Execute(CancellationToken token) {} }")]
    public async Task UnsupportedStepConstructorsAreRejected(string declaration)
    {
        var generated = await Generate(declaration);
        await Assert.That(generated.Diagnostics.Any(d => d.Id == "TTP013")).IsTrue();
    }
}

