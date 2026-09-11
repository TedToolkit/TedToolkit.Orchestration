using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments("new StepGraph().Work()", "TTP014", DiagnosticSeverity.Warning)]
    [Arguments("WorkStepMethods_WorkExtensions.Work(new StepGraph())", "TTP014", DiagnosticSeverity.Warning)]
    [Arguments("default(StepBuilder<int>).WithRetry(1)", "TTP017", DiagnosticSeverity.Warning)]
    public async Task ConfigurationOnlyApisReportActionableDiagnostics(
        string call, string id, DiagnosticSeverity severity)
    {
        var generated = await Generate("""
            internal static class WorkStepMethods
            {
                [Step]
                internal static int Work(CancellationToken token) => 1;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph graph) { var work = graph.Work(); }
            }
            public static class Consumer { public static void Run() { CALL; } }
            """.Replace("CALL", call));
        await NoErrors(generated);
        var diagnostic = generated.Diagnostics.Single(item => item.Id == id);
        await Assert.That(diagnostic.Severity).IsEqualTo(severity);
    }

    [Test]
    public async Task DirectCompositeConfigurationInvocationReportsTtp015()
    {
        var generated = await Generate("""
            public static partial class Example
            {
                public static void Configuration(StepGraph graph) { }
            }
            public static class Consumer
            {
                public static void Run() => Example.Configuration(new StepGraph());
            }
            """);

        var diagnostic = generated.Diagnostics.Single(item => item.Id == "TTP015");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.GetMessage()).Contains("generated Pipeline Execute method");
        await Assert.That(diagnostic.Location.SourceTree!.GetText()
            .ToString(diagnostic.Location.SourceSpan))
            .IsEqualTo("Example.Configuration(new StepGraph())");
        await Assert.That(string.Join("\n", generated.Diagnostics.Where(item =>
            item.Severity == DiagnosticSeverity.Error && item.Id != "TTP015")))
            .IsEqualTo("");
    }

    [Test]
    public async Task ReferencedCompositeConfigurationInvocationReportsTtp015()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var producer = await Generate("""
            public static partial class External
            {
                public static void Configuration(StepGraph graph) { }
            }
            """, assemblyName: "ConfigurationProducer_" + suffix);
        await NoErrors(producer);

        var consumer = await Generate("""
            public static class Consumer
            {
                public static void Run() => External.Configuration(new StepGraph());
            }
            """, MetadataReference.CreateFromImage(Emit(producer)),
            "ConfigurationConsumer_" + suffix);

        var diagnostic = consumer.Diagnostics.Single(item => item.Id == "TTP015");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.GetMessage()).Contains("generated Pipeline Execute method");
        await Assert.That(diagnostic.Location.SourceTree!.GetText()
            .ToString(diagnostic.Location.SourceSpan))
            .IsEqualTo("External.Configuration(new StepGraph())");
        await Assert.That(string.Join("\n", consumer.Diagnostics.Where(item =>
            item.Severity == DiagnosticSeverity.Error && item.Id != "TTP015")))
            .IsEqualTo("");
    }

    [Test]
    public async Task DirectLeafExecutionIsAnOrdinaryStaticCallAndDoesNotWarn()
    {
        var generated = await Generate("""
            internal static class RawStepMethods
            {
                [Step]
                internal static void Raw(CancellationToken token) { }
            }
            public static class Consumer
            {
                public static void Run() => RawStepMethods.Raw(default);
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
            internal static class WorkStepMethods
            {
                [Step]
                internal static int Work(CancellationToken token) => 1;
            }
            public static partial class Example
            {
                public static void Configuration(GraphBuilder builder) { var work = builder.Work(); }
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.Compilation.GetTypeByMetadataName("Example")!
            .GetMembers("Configuration").Length).IsEqualTo(2);
        await Assert.That(generated.Diagnostics.Any(item =>
            item.Id is "TTP014" or "TTP015" or "TTP017")).IsFalse();
    }
}
