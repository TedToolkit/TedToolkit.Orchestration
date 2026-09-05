using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments("new Pipeline.Builder().Work()", "TTP014", DiagnosticSeverity.Warning)]
    [Arguments("WorkExtensions.Work(new Pipeline.Builder())", "TTP014", DiagnosticSeverity.Warning)]
    [Arguments("new Work().Execute(default)", "TTP016", DiagnosticSeverity.Info)]
    public async Task RuntimeMisuseReportsAnActionableDiagnostic(string call, string id, DiagnosticSeverity severity)
    {
        var generated = await Generate("""
            [StepPolicy(RetryCount = 1)]
            internal readonly ref struct Work : IStep<int>
            {
                public int Execute(CancellationToken token) => 1;
            }
            public static class Consumer { public static void Run() { CALL; } }
            """.Replace("CALL", call));
        await NoErrors(generated);
        var diagnostic = generated.Diagnostics.Single(item => item.Id == id);
        await Assert.That(diagnostic.Severity).IsEqualTo(severity);
        await Assert.That(diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)).IsEqualTo(call);
    }

    [Test]
    [Arguments("Configuration")]
    [Arguments("Configure")]
    public async Task CallingConfigurationWarnsInsteadOfSuggestingThatItRunsWork(string name)
    {
        var declaration = name == "Configuration" ? "protected override void Configuration" : "private void Configure";
        var generated = await Generate("""
            public sealed partial class Example : Pipeline
            {
                DECLARATION(Builder builder) { }
                public void Run() => NAME(default);
            }
            """.Replace("DECLARATION", declaration).Replace("NAME", name));
        await NoErrors(generated);
        await Assert.That(generated.Diagnostics.Count(item => item.Id == "TTP015")).IsEqualTo(1);
    }

    [Test]
    public async Task ValidDeclarationsAndRawStepsWithoutPoliciesDoNotWarn()
    {
        var generated = await Generate("""
            [StepPolicy(RetryCount = 1)]
            internal readonly ref struct Work : IStep<int> { public int Execute(CancellationToken token) => 1; }
            internal readonly ref struct Raw : IStep { public void Execute(CancellationToken token) { } }
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder builder) { builder.Work(); }
                public void RawCall() => new Raw().Execute(default);
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.Diagnostics.Any(item => item.Id is "TTP014" or "TTP015" or "TTP016")).IsFalse();
    }

    [Test]
    public async Task UnrelatedNamesDoNotTriggerPipelineUsageDiagnostics()
    {
        var generated = await Generate("""
            public sealed class Builder { }
            public sealed class Other
            {
                public void Configuration(Builder builder) { }
                public void Execute() { }
                public void Run() { Configuration(new Builder()); Execute(); }
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.Diagnostics.Any(item => item.Id.StartsWith("TTP"))).IsFalse();
    }

    [Test]
    public async Task BuilderAliasesUseSemanticIdentityAndStillGenerateExecution()
    {
        var generated = await Generate("""
            using GraphBuilder = TedToolkit.Orchestration.Pipeline.Pipeline.Builder;
            internal readonly ref struct Work : IStep<int> { public int Execute(CancellationToken token) => 1; }
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(GraphBuilder builder) { builder.Work(); }
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.Compilation.GetTypeByMetadataName("Example")!.GetMembers("Execute").Length).IsEqualTo(1);
        await Assert.That(generated.Diagnostics.Any(item => item.Id is "TTP014" or "TTP015" or "TTP016")).IsFalse();
    }
}
