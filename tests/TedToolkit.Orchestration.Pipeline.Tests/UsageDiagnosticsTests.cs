using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments("new StepGraph().Work()", "TTP014", DiagnosticSeverity.Warning)]
    [Arguments("WorkExtensions.Work(new StepGraph())", "TTP014", DiagnosticSeverity.Warning)]
    [Arguments("default(StepBuilder<int>).WithRetry(1)", "TTP017", DiagnosticSeverity.Warning)]
    public async Task ConfigurationOnlyApisReportActionableDiagnostics(
        string call, string id, DiagnosticSeverity severity)
    {
        var generated = await Generate("""
            internal readonly ref partial struct Work : IStep<int>
            {
                public int Execute(CancellationToken token) => 1;
            }
            [CompositeStep]
            public readonly ref partial struct Example
            {
                private void Configuration(StepGraph graph) { var work = graph.Work(); }
            }
            public static class Consumer { public static void Run() { CALL; } }
            """.Replace("CALL", call));
        await NoErrors(generated);
        var diagnostic = generated.Diagnostics.Single(item => item.Id == id);
        await Assert.That(diagnostic.Severity).IsEqualTo(severity);
    }

    [Test]
    public async Task DirectLeafExecutionRequiresGeneratedContextButDoesNotWarn()
    {
        var generated = await Generate("""
            internal readonly ref partial struct Raw : IStep
            {
                public void Execute(CancellationToken token) { }
            }
            public static class Consumer
            {
                public static void Run() => new Raw { DisplayName = "Direct" }.Execute(default);
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.Diagnostics.Any(item =>
            item.Id is "TTP014" or "TTP015" or "TTP017")).IsFalse();
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
    public async Task StepGraphAliasesUseSemanticIdentityAndStillGenerateExecution()
    {
        var generated = await Generate("""
            using GraphBuilder = TedToolkit.Orchestration.Pipeline.StepGraph;
            internal readonly ref partial struct Work : IStep<int>
            {
                public int Execute(CancellationToken token) => 1;
            }
            [CompositeStep]
            public readonly ref partial struct Example
            {
                private void Configuration(GraphBuilder builder) { var work = builder.Work(); }
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.Compilation.GetTypeByMetadataName("Example")!
            .GetMembers("Execute").Length).IsEqualTo(1);
        await Assert.That(generated.Diagnostics.Any(item =>
            item.Id is "TTP014" or "TTP015" or "TTP017")).IsFalse();
    }
}
