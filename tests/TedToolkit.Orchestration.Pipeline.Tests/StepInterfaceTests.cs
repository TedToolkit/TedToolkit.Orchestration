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
    [Arguments("RetryCount = -1")]
    [Arguments("TimeoutMilliseconds = 0")]
    [Arguments("TimeoutMilliseconds = -2")]
    public async Task InvalidPoliciesAreRejectedAtCompileTime(string policy)
    {
        var generated = await Generate("[StepPolicy(" + policy + ")] internal readonly ref struct Bad : IStep { public void Execute(CancellationToken token) {} }");
        await Assert.That(generated.Diagnostics.Any(d => d.Id == "TTP013")).IsTrue();
    }

    [Test]
    public async Task RefStructStepsCannotBeBoxedAsTheirInterface()
    {
        var generated = await Generate("""
            internal readonly ref struct Node : IAsyncStep<int> { public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(1); }
            """ + Scenario("IAsyncStep<int> boxed = new Node(); return Task.FromResult(string.Empty);"));
        await Assert.That(generated.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS"))).IsTrue();
    }
}

