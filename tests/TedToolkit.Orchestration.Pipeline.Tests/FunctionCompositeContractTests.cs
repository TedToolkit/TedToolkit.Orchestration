using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ReferencedCompositeRejectsUnsupportedAndMalformedProtocols()
    {
        var cases = new Dictionary<string, string>
        {
            ["unsupported version"] = ProtocolSource(version: 2),
            ["duplicate marker"] = ProtocolSource(extraExecute: """
                [global::TedToolkit.Orchestration.Pipeline.CompilerServices.GeneratedCompositeStep(1, false)]
                public static Results __TedToolkitExecuteCompositeStep(
                    __TedToolkitCompositeStepState state,
                    int value,
                    global::System.Threading.CancellationToken cancellationToken = default) => default;
                """),
            ["non-static owner"] = ProtocolSource(owner: "public class External"),
            ["mutable state"] = ProtocolSource(state: "public struct __TedToolkitCompositeStepState"),
            ["inaccessible prepare"] = ProtocolSource(prepareAccessibility: "private"),
            ["inaccessible execute"] = ProtocolSource(executeAccessibility: "private"),
            ["token without default"] = ProtocolSource(tokenDefault: ""),
            ["missing Results"] = """
                public static class External
                {
                    public readonly struct __TedToolkitCompositeStepState { }
                    public static __TedToolkitCompositeStepState __TedToolkitPrepareCompositeStep(string path) => default;
                    [global::TedToolkit.Orchestration.Pipeline.CompilerServices.GeneratedCompositeStep(1, false)]
                    public static int __TedToolkitExecuteCompositeStep(
                        __TedToolkitCompositeStepState state,
                        global::System.Threading.CancellationToken token = default) => 0;
                }
                """,
            ["mutable Results"] = ProtocolSource(results: "public struct Results"),
            ["wrong prepare return"] = ProtocolSource(prepareReturn: "int"),
            ["wrong prepare provider"] = ProtocolSource(
                requiresServices: true,
                prepareParameters: "int services, string displayPath"),
            ["wrong execute state"] = ProtocolSource(executeState: "int"),
            ["wrong execute return"] = ProtocolSource(executeReturn: "int"),
            ["marker on wrong member"] = """
                public static class External
                {
                    public readonly struct Results { }
                    public readonly struct __TedToolkitCompositeStepState { }
                    public static __TedToolkitCompositeStepState __TedToolkitPrepareCompositeStep(string path) => default;
                    [global::TedToolkit.Orchestration.Pipeline.CompilerServices.GeneratedCompositeStep(1, false)]
                    public static void WrongMember() { }
                    public static Results __TedToolkitExecuteCompositeStep(
                        __TedToolkitCompositeStepState state,
                        global::System.Threading.CancellationToken token = default) => default;
                }
                """,
        };

        foreach (var item in cases)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var producer = await Generate(item.Value, assemblyName: "InvalidProtocolProducer_" + suffix);
            await NoErrors(producer);
            var producerImage = Emit(producer);
            var consumer = await Generate("""
                public static partial class Consumer
                {
                    public static void Configuration(StepGraph steps)
                    {
                        steps.External();
                    }
                }
                """, MetadataReference.CreateFromImage(producerImage), "InvalidProtocolConsumer_" + suffix);

            await Assert.That(consumer.Diagnostics.Any(diagnostic =>
                diagnostic.Id == "TTP019")).IsTrue().Because(item.Key);
            await Assert.That(consumer.GeneratedSource).DoesNotContain(
                "global::External.__TedToolkitExecuteCompositeStep").Because(item.Key);
            await Assert.That(consumer.GeneratedSource).DoesNotContain("ICompositeStep").Because(item.Key);
        }
    }

    [Test]
    public async Task GeneratedProtocolAndFacadeNamesAvoidUserParameterNames()
    {
        var generated = await Generate("""
            public static partial class LeafCollisions
            {
                [Step, Pipeline]
                public static int Echo(int __result, CancellationToken __result_ = default) => __result;
            }

            public sealed class Offset { public int Value => 2; }

            internal static class ValueSteps
            {
                [Step]
                internal static int Value(int value, CancellationToken token = default) => value;
            }

            public static partial class CompositeCollisions
            {
                [Pipeline]
                public static void Configuration(
                    StepGraph steps,
                    int __state,
                    int __state_,
                    int __services,
                    int __displayPath,
                    int cancellationToken,
                    [FromServices] Offset offset = null!,
                    CancellationToken __state__ = default)
                {
                    var output = steps.Value(
                        __state + __state_ + __services + __displayPath + cancellationToken + offset.Value);
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var leaf = new LeafCollisions.EchoPipeline().Execute(42);
                    var services = new ServiceCollection().AddSingleton<Offset>().BuildServiceProvider();
                    var composite = new CompositeCollisions.ConfigurationPipeline(services)
                        .Execute(1, 2, 3, 4, 5);
                    return Task.FromResult($"{leaf}:{composite.Output}");
                }
            }
            """);

        await NoErrors(generated);
        var image = Emit(generated);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image));
        var run = assembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<Func<Task<string>>>();

        await Assert.That(await run()).IsEqualTo("42:17");
        await Assert.That(generated.GeneratedSource).Contains("__result__");
        await Assert.That(generated.GeneratedSource).Contains("__state___");
        await Assert.That(generated.GeneratedSource).Contains("__services_");
        await Assert.That(generated.GeneratedSource).Contains("__displayPath_");
    }

    [Test]
    public async Task IdenticalMetadataNamesFromAliasedAssembliesCanBeSelectedExactly()
    {
        var alpha = await Generate(SameFqnProducer("value + 1"), assemblyName: "SameFqnAlpha");
        var beta = await Generate(SameFqnProducer("value + 2"), assemblyName: "SameFqnBeta");
        await NoErrors(alpha);
        await NoErrors(beta);
        var alphaImage = Emit(alpha);
        var betaImage = Emit(beta);
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(alphaImage));
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(betaImage));
        var alphaReference = MetadataReference.CreateFromImage(alphaImage,
            MetadataReferenceProperties.Assembly.WithAliases(["AlphaRef"]));
        var betaReference = MetadataReference.CreateFromImage(betaImage,
            MetadataReferenceProperties.Assembly.WithAliases(["BetaRef"]));

        var consumer = await GenerateWithReferences("""
            public static partial class Consumer
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value)
                {
                    var first = SameFqnAlpha_Shared_TransformExtensions.Transform(steps, value);
                    var second = SameFqnBeta_Shared_TransformExtensions.Transform(steps, value);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Consumer.ConfigurationPipeline().Execute(1);
                    return Task.FromResult($"{result.First.Output}:{result.Second.Output}");
                }
            }
            """, "SameFqnConsumer", alphaReference, betaReference);

        await NoErrors(consumer);
        await Assert.That(consumer.GeneratedSource).Contains("extern alias AlphaRef;");
        await Assert.That(consumer.GeneratedSource).Contains("extern alias BetaRef;");
        var consumerImage = Emit(consumer);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(consumerImage));
        var run = assembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<Func<Task<string>>>();
        await Assert.That(await run()).IsEqualTo("2:3");
    }

    [Test]
    public async Task IdenticalMetadataNamesWithoutAliasesReportTtp019()
    {
        var alpha = await Generate(SameFqnProducer("value + 1"), assemblyName: "UnaliasedAlpha");
        var beta = await Generate(SameFqnProducer("value + 2"), assemblyName: "UnaliasedBeta");
        await NoErrors(alpha);
        await NoErrors(beta);
        var consumer = await GenerateWithReferences("""
            public static partial class Consumer
            {
                public static void Configuration(StepGraph steps, int value)
                {
                    var first = UnaliasedAlpha_Shared_TransformExtensions.Transform(steps, value);
                    var second = UnaliasedBeta_Shared_TransformExtensions.Transform(steps, value);
                }
            }
            """, "UnaliasedConsumer", MetadataReference.CreateFromImage(Emit(alpha)),
            MetadataReference.CreateFromImage(Emit(beta)));

        await Assert.That(consumer.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP019" && diagnostic.GetMessage().Contains(
                "extern alias", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AmbiguousUnqualifiedFactoryReportsActionableStaticGraphDiagnostic()
    {
        var alpha = await Generate(NamespaceProducer("Alpha", "value + 1"), assemblyName: "AmbiguousAlpha");
        var beta = await Generate(NamespaceProducer("Beta", "value + 2"), assemblyName: "AmbiguousBeta");
        await NoErrors(alpha);
        await NoErrors(beta);
        var consumer = await GenerateWithReferences("""
            public static partial class Consumer
            {
                public static void Configuration(StepGraph steps, int value)
                {
                    var output = steps.Transform(value);
                }
            }
            """, "AmbiguousConsumer", MetadataReference.CreateFromImage(Emit(alpha)),
            MetadataReference.CreateFromImage(Emit(beta)));

        await Assert.That(consumer.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP009" && diagnostic.GetMessage().Contains(
                "ambiguous", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task PublicCompositeProtocolMembersHaveGeneratedDocumentation()
    {
        var generated = await Generate("""
            internal static class ValueSteps
            {
                [Step]
                internal static int Value(CancellationToken token = default) => 42;
            }
            public static partial class Documented
            {
                public static void Configuration(StepGraph steps)
                {
                    var value = steps.Value();
                }
            }
            """);
        await NoErrors(generated);

        AssertSummaryBefore(generated.GeneratedSource, "public readonly struct __TedToolkitCompositeStepState");
        AssertSummaryBefore(generated.GeneratedSource, "public static __TedToolkitCompositeStepState __TedToolkitPrepareCompositeStep");
        AssertSummaryBefore(generated.GeneratedSource, "public static Results __TedToolkitExecuteCompositeStep");
    }

    [Test]
    public async Task PipelineFacadeContractCoversKindsNamesVisibilityAndMethodShapes()
    {
        var generated = await Generate("""
            public static partial class PublicLeafSteps
            {
                [Step, Pipeline]
                public static int Run(int value, CancellationToken token = default) => value;

                [Step]
                public static int Unmarked(CancellationToken token = default) => 0;
            }

            internal static partial class InternalLeafSteps
            {
                [Step, Pipeline]
                internal static Task<int> Load(CancellationToken token = default) => Task.FromResult(1);
            }

            internal static class ValueSteps
            {
                [Step]
                internal static int Value(CancellationToken token = default) => 42;
            }

            public static partial class PublicComposite
            {
                [Pipeline(Name = "Import")]
                public static void Configuration(StepGraph steps)
                {
                    var value = steps.Value();
                }
            }

            internal static partial class InternalComposite
            {
                [Pipeline]
                internal static void Configuration(StepGraph steps)
                {
                    var value = steps.Value();
                }
            }
            """);
        await NoErrors(generated);

        var publicLeaf = generated.Compilation.GetTypeByMetadataName("PublicLeafSteps")!;
        var publicLeafPipeline = publicLeaf.GetTypeMembers("RunPipeline").Single();
        await Assert.That(publicLeafPipeline.DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(publicLeafPipeline.GetMembers("Execute").Length).IsEqualTo(1);
        await Assert.That(publicLeafPipeline.GetMembers("ExecuteWithoutResults").Length).IsEqualTo(1);
        await Assert.That(publicLeaf.GetTypeMembers("UnmarkedPipeline").Length).IsEqualTo(0);

        var internalLeafPipeline = generated.Compilation.GetTypeByMetadataName("InternalLeafSteps")!
            .GetTypeMembers("LoadPipeline").Single();
        await Assert.That(internalLeafPipeline.DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(internalLeafPipeline.GetMembers("ExecuteAsync").Length).IsEqualTo(1);
        await Assert.That(internalLeafPipeline.GetMembers("ExecuteWithoutResultsAsync").Length).IsEqualTo(1);

        var publicCompositePipeline = generated.Compilation.GetTypeByMetadataName("PublicComposite")!
            .GetTypeMembers("ImportPipeline").Single();
        await Assert.That(publicCompositePipeline.DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(publicCompositePipeline.GetMembers("Execute").Length).IsEqualTo(1);
        await Assert.That(publicCompositePipeline.GetMembers("ExecuteWithoutResults").Length).IsEqualTo(1);

        var internalCompositePipeline = generated.Compilation.GetTypeByMetadataName("InternalComposite")!
            .GetTypeMembers("ConfigurationPipeline").Single();
        await Assert.That(internalCompositePipeline.DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
    }

    [Test]
    public async Task InvalidPipelineNamesOwnersAndCollisionsReportTtp018()
    {
        var cases = new Dictionary<string, string>
        {
            ["empty stem"] = """
                public static partial class Steps
                {
                    [Step, Pipeline(Name = "")]
                    public static int Run(CancellationToken token = default) => 0;
                }
                """,
            ["invalid identifier"] = """
                public static partial class Steps
                {
                    [Step, Pipeline(Name = "bad-name")]
                    public static int Run(CancellationToken token = default) => 0;
                }
                """,
            ["stem has suffix"] = """
                public static partial class Steps
                {
                    [Step, Pipeline(Name = "RunPipeline")]
                    public static int Run(CancellationToken token = default) => 0;
                }
                """,
            ["owner is not partial"] = """
                public static class Steps
                {
                    [Step, Pipeline]
                    public static int Run(CancellationToken token = default) => 0;
                }
                """,
            ["user member collision"] = """
                public static partial class Steps
                {
                    public sealed class RunPipeline { }
                    [Step, Pipeline]
                    public static int Run(CancellationToken token = default) => 0;
                }
                """,
            ["duplicate generated name"] = """
                public static partial class Steps
                {
                    [Step, Pipeline(Name = "Same")]
                    public static int First(CancellationToken token = default) => 0;
                    [Step, Pipeline(Name = "Same")]
                    public static int Second(CancellationToken token = default) => 0;
                }
                """,
        };

        foreach (var item in cases)
        {
            var generated = await Generate(item.Value);
            await Assert.That(generated.Diagnostics.Any(diagnostic =>
                diagnostic.Id == "TTP018")).IsTrue().Because(item.Key);
        }
    }

    [Test]
    [Arguments("public readonly struct Results { }")]
    [Arguments("public readonly struct __TedToolkitCompositeStepState { }")]
    [Arguments("public static void __TedToolkitPrepareCompositeStep() { }")]
    [Arguments("public static void __TedToolkitExecuteCompositeStep() { }")]
    public async Task LocalCompositeReservedMembersReportTtp009WithoutGeneratedFallback(
        string reservedMember)
    {
        var generated = await Generate($$"""
            public static partial class ReservedComposite
            {
                {{reservedMember}}

                [Pipeline]
                public static void Configuration(StepGraph steps) { }
            }
            """);

        var diagnostics = generated.Diagnostics.Where(item => item.Id == "TTP009").ToArray();
        await Assert.That(diagnostics.Length > 0).IsTrue();
        await Assert.That(diagnostics.All(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage().Contains("reserved for generated execution",
                StringComparison.Ordinal))).IsTrue();
        await Assert.That(generated.GeneratedSource).DoesNotContain(
            "partial class ReservedComposite");
        await Assert.That(generated.GeneratedSource).DoesNotContain(
            "sealed class ConfigurationPipeline");
    }

    [Test]
    public async Task ReferencedCompositeExecutesDefaultsServicesLoggerRetryAndCancellationFromMetadata()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var producer = await Generate("""
            namespace ProtocolProducer
            {
                public sealed class DirectOffset { public int Value => 1; }
                public sealed class KeyedOffset { public int Value => 2; }

                public static class Counters
                {
                    public static int DirectResolutions;
                    public static int KeyedResolutions;
                    public static int LoggerCreations;
                    public static int Attempts;
                    public static string Category = "";
                }

                public sealed class CaptureLoggerFactory : ILoggerFactory
                {
                    public ILogger CreateLogger(string categoryName)
                    {
                        Counters.Category = categoryName;
                        Counters.LoggerCreations++;
                        return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                    }
                    public void AddProvider(ILoggerProvider provider) { }
                    public void Dispose() { }
                }

                internal static class WorkSteps
                {
                    [Step]
                    internal static int Eventually(int value, CancellationToken token)
                    {
                        token.ThrowIfCancellationRequested();
                        if (++Counters.Attempts == 1) throw new InvalidOperationException("retry");
                        return value;
                    }
                }

                public static partial class External
                {
                    public static void Configuration(
                        StepGraph steps,
                        int value,
                        int increment = 1,
                        [FromServices] DirectOffset direct = null!,
                        [FromServices("keyed")] KeyedOffset keyed = null!,
                        [FromServices] ILogger logger = null!,
                        CancellationToken token = default)
                    {
                        var output = steps.Eventually(value + increment + direct.Value + keyed.Value);
                    }
                }
            }
            """, assemblyName: "CompositeProtocolProducer_" + suffix);
        await NoErrors(producer);
        var producerImage = Emit(producer);
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(producerImage));

        var consumer = await Generate("""
            public static partial class Consumer
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value)
                {
                    var external = steps.External(value)
                        .WithDisplayName("Remote")
                        .WithRetry(1);
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var services = new ServiceCollection()
                        .AddTransient<ProtocolProducer.DirectOffset>(_ =>
                        {
                            ProtocolProducer.Counters.DirectResolutions++;
                            return new ProtocolProducer.DirectOffset();
                        })
                        .AddKeyedTransient<ProtocolProducer.KeyedOffset>("keyed", (_, _) =>
                        {
                            ProtocolProducer.Counters.KeyedResolutions++;
                            return new ProtocolProducer.KeyedOffset();
                        })
                        .AddSingleton<ILoggerFactory, ProtocolProducer.CaptureLoggerFactory>()
                        .BuildServiceProvider();
                    var pipeline = new Consumer.ConfigurationPipeline(services);
                    var result = pipeline.Execute(40);
                    using var cancellation = new CancellationTokenSource();
                    cancellation.Cancel();
                    var canceled = false;
                    try { pipeline.Execute(40, cancellationToken: cancellation.Token); }
                    catch (OperationCanceledException failure)
                    {
                        canceled = failure.CancellationToken == cancellation.Token;
                    }
                    return Task.FromResult($"{result.External.Output}:{ProtocolProducer.Counters.Attempts}:" +
                        $"{ProtocolProducer.Counters.DirectResolutions}:{ProtocolProducer.Counters.KeyedResolutions}:" +
                        $"{ProtocolProducer.Counters.LoggerCreations}:{ProtocolProducer.Counters.Category}:{canceled}");
                }
            }
            """, MetadataReference.CreateFromImage(producerImage), "CompositeProtocolConsumer_" + suffix);
        await NoErrors(consumer);
        var consumerImage = Emit(consumer);
        var consumerAssembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(consumerImage));
        var run = consumerAssembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<Func<Task<string>>>();

        await Assert.That(await run()).IsEqualTo(
            "44:2:1:1:1:ProtocolProducer.External.Configuration[Consumer/Remote]:True");
    }

    private static string ProtocolSource(
        int version = 1,
        string owner = "public static class External",
        string results = "public readonly struct Results",
        string state = "public readonly struct __TedToolkitCompositeStepState",
        string prepareAccessibility = "public",
        string prepareReturn = "__TedToolkitCompositeStepState",
        string prepareParameters = "string displayPath",
        string executeAccessibility = "public",
        string executeReturn = "Results",
        string executeState = "__TedToolkitCompositeStepState",
        bool requiresServices = false,
        string tokenDefault = " = default",
        string extraExecute = "") => $$"""
        {{owner}}
        {
            {{results}} { }
            {{state}} { }
            {{prepareAccessibility}} static {{prepareReturn}} __TedToolkitPrepareCompositeStep(
                {{prepareParameters}}) => default;
            [global::TedToolkit.Orchestration.Pipeline.CompilerServices.GeneratedCompositeStep({{version}}, {{requiresServices.ToString().ToLowerInvariant()}})]
            {{executeAccessibility}} static {{executeReturn}} __TedToolkitExecuteCompositeStep(
                {{executeState}} state,
                global::System.Threading.CancellationToken cancellationToken{{tokenDefault}}) => default;
            {{extraExecute}}
        }
        """;

    private static string SameFqnProducer(string expression) => $$"""
        namespace Shared
        {
            internal static class WorkSteps
            {
                [Step]
                internal static int Change(int value, CancellationToken token = default) => {{expression}};
            }
            public static partial class Transform
            {
                public static void Configuration(StepGraph steps, int value)
                {
                    var output = steps.Change(value);
                }
            }
        }
        """;

    private static string NamespaceProducer(string nameSpace, string expression) => $$"""
        namespace {{nameSpace}}
        {
            internal static class WorkSteps
            {
                [Step]
                internal static int Change(int value, CancellationToken token = default) => {{expression}};
            }
            public static partial class Transform
            {
                public static void Configuration(StepGraph steps, int value)
                {
                    var output = steps.Change(value);
                }
            }
        }
        """;

    private static void AssertSummaryBefore(string source, string declaration)
    {
        var declarationIndex = source.IndexOf(declaration, StringComparison.Ordinal);
        if (declarationIndex < 0) throw new InvalidOperationException("Missing generated declaration: " + declaration);
        var summaryIndex = source.LastIndexOf("/// <summary>", declarationIndex, StringComparison.Ordinal);
        if (summaryIndex < 0 || declarationIndex - summaryIndex > 400)
            throw new InvalidOperationException("Missing generated summary for: " + declaration);
    }

    private static byte[] Emit(GeneratedCompilation generated)
    {
        using var stream = new MemoryStream();
        var result = generated.Compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return stream.ToArray();
    }
}
