using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ArbitraryUnattributedCompositeIsDiscoveredAcrossAssembliesWithoutState()
    {
        var producer = await Generate("""
            internal static class ValueSteps
            {
                [Step]
                internal static int Value(int value, CancellationToken token = default) => value;
            }

            public static partial class External
            {
                public static void Build(StepGraph steps, int value)
                {
                    var output = steps.Value(value);
                }
            }
            """, assemblyName: "ArbitraryCompositeProducer");
        await NoErrors(producer);
        await Assert.That(producer.GeneratedSource).Contains("readonly struct BuildResult");
        await Assert.That(producer.GeneratedSource).DoesNotContain("__TedToolkitCompositeStepState");
        await Assert.That(producer.GeneratedSource).DoesNotContain("__TedToolkitPrepareCompositeStep");

        var producerImage = Emit(producer);
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(producerImage));
        var consumer = await Generate("""
            public static partial class Root
            {
                [Pipeline]
                public static void Run(StepGraph steps, int value)
                {
                    var nested = steps.Build(value);
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Root.RunPipeline().Execute(42);
                    return Task.FromResult(result.Nested.Output.ToString());
                }
            }
            """, MetadataReference.CreateFromImage(producerImage), "ArbitraryCompositeConsumer");
        await NoErrors(consumer);
        var consumerImage = Emit(consumer);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(consumerImage));
        var run = assembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<Func<Task<string>>>();

        await Assert.That(await run()).IsEqualTo("42");
        await Assert.That(consumer.GeneratedSource).DoesNotContain("External.BuildPipeline");
    }

    [Test]
    public async Task StructuralCompositeProtocolHasDeterministicMembership()
    {
        var acceptedReference = CompilePlainReference(StructuralProtocolSource("""
            public static BuildResult Build(string path, int value, CancellationToken token = default) => default;
            public static void Build(DateTime unrelated) { }
            """), "AcceptedStructuralProtocol");
        var accepted = await Generate("""
            public static partial class AcceptedConsumer
            {
                [Pipeline]
                public static void Run(StepGraph steps)
                {
                    var external = steps.Build(1);
                }
            }
            """, acceptedReference, "AcceptedStructuralProtocolConsumer");
        await NoErrors(accepted);
        await Assert.That(accepted.GeneratedSource).Contains("global::External.Build(");

        var sameNameReference = CompilePlainReference("""
            public static class Broken
            {
                public static void Parse(StepGraph steps, int value) { }
            }
            """, "UnrelatedInvocationProtocol_" + Guid.NewGuid().ToString("N"));
        var unrelated = await Generate("""
            internal static class Values
            {
                [Step]
                internal static int Value(int value, CancellationToken token = default) => value;
            }

            public static partial class UnrelatedConsumer
            {
                public static void Build(StepGraph steps)
                {
                    var output = steps.Value(int.Parse("42"));
                }
            }
            """, sameNameReference, "UnrelatedInvocationConsumer_" + Guid.NewGuid().ToString("N"));
        await NoErrors(unrelated);
        await Assert.That(unrelated.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP019")).IsFalse();

        var requestedMalformed = await Generate("""
            public static partial class RequestedConsumer
            {
                public static void Build(StepGraph steps)
                {
                    var parsed = steps.Parse(1);
                }
            }
            """, sameNameReference, "RequestedMalformedConsumer_" + Guid.NewGuid().ToString("N"));
        await Assert.That(requestedMalformed.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP019" && diagnostic.GetMessage().Contains(
                "Broken.Parse", StringComparison.Ordinal))).IsTrue();
        await Assert.That(requestedMalformed.GeneratedSource).DoesNotContain("global::Broken.Parse(");

        var cases = new Dictionary<string, string>
        {
            ["missing execute"] = StructuralProtocolSource(""),
            ["wrong return"] = StructuralProtocolSource(
                "public static int Build(string path, int value, CancellationToken token = default) => 0;"),
            ["wrong input"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, long value, CancellationToken token = default) => default;"),
            ["token without default"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, CancellationToken token) => default;"),
            ["mutable result"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, CancellationToken token = default) => default;",
                "public struct BuildResult"),
            ["duplicate exact matches"] = StructuralProtocolSource("""
                public static BuildResult Build(string path, int value, CancellationToken token = default) => default;
                public static BuildResult Build(IServiceProvider services, string path, int value, CancellationToken token = default) => default;
                """),
            ["service declaration without protocol provider"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, [FromServices] IServiceProvider service, CancellationToken token = default) => default;",
                declaration: "public static void Build(StepGraph steps, int value, [FromServices] IServiceProvider service) { }"),
            ["misplaced declaration token"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, CancellationToken token = default) => default;",
                declaration: "public static void Build(StepGraph steps, CancellationToken token, int value) { }"),
            ["multiple declaration tokens"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, CancellationToken token = default) => default;",
                declaration: "public static void Build(StepGraph steps, CancellationToken first, int value, CancellationToken second) { }"),
            ["service-marked declaration token"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, CancellationToken token = default) => default;",
                declaration: "public static void Build(StepGraph steps, int value, [FromServices] CancellationToken token) { }"),
            ["service-marked execute token"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, [FromServices] CancellationToken token = default) => default;"),
            ["extra declaration graph"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, StepGraph other, CancellationToken token = default) => default;",
                declaration: "public static void Build(StepGraph steps, int value, StepGraph other) { }"),
            ["ref declaration input"] = StructuralProtocolSource(
                "public static BuildResult Build(string path, int value, CancellationToken token = default) => default;",
                declaration: "public static void Build(StepGraph steps, ref int value) { }"),
        };

        foreach (var item in cases)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var producer = CompilePlainReference(item.Value, "InvalidProtocolProducer_" + suffix);
            var consumer = await Generate("""
                public static partial class Consumer
                {
                    public static void Configuration(StepGraph steps)
                    {
                        steps.Build(1);
                    }
                }
                """, producer, "InvalidProtocolConsumer_" + suffix);

            await Assert.That(consumer.Diagnostics.Any(diagnostic =>
                diagnostic.Id == "TTP019")).IsTrue().Because(item.Key);
            await Assert.That(consumer.GeneratedSource).DoesNotContain(
                "global::External.Build(").Because(item.Key);
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

                [Step]
                internal static Task<int> DelayValue(int value, CancellationToken token = default) =>
                    Task.FromResult(value);
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

            public static partial class SequentialLocalCollisions
            {
                [Pipeline]
                public static void Build(StepGraph steps, int result0)
                {
                    var output = steps.Value(result0);
                }
            }

            public static partial class ParallelLocalCollisions
            {
                [Pipeline]
                public static void Build(StepGraph steps, int execution, int task0)
                {
                    var first = steps.DelayValue(execution);
                    var second = steps.DelayValue(task0);
                }
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var leaf = new LeafCollisions.EchoPipeline().Execute(42);
                    var services = new ServiceCollection().AddSingleton<Offset>().BuildServiceProvider();
                    var composite = new CompositeCollisions.ConfigurationPipeline(services)
                        .Execute(1, 2, 3, 4, 5);
                    var sequential = new SequentialLocalCollisions.BuildPipeline().Execute(7);
                    var parallel = await new ParallelLocalCollisions.BuildPipeline().ExecuteAsync(8, 9);
                    return $"{leaf}:{composite.Output}:{sequential.Output}:{parallel.First}:{parallel.Second}";
                }
            }
            """);

        await NoErrors(generated);
        var image = Emit(generated);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image));
        var run = assembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<Func<Task<string>>>();

        await Assert.That(await run()).IsEqualTo("42:17:7:8:9");
        await Assert.That(generated.GeneratedSource).Contains("__result__");
        await Assert.That(generated.GeneratedSource).Contains("__services_");
        await Assert.That(generated.GeneratedSource).Contains("__displayPath_");
        await Assert.That(generated.GeneratedSource).Contains("result0_");
        await Assert.That(generated.GeneratedSource).Contains("execution_");
        await Assert.That(generated.GeneratedSource).Contains("task0_");
        await Assert.That(generated.GeneratedSource).DoesNotContain("__TedToolkitCompositeStepState");
    }

    [Test]
    public async Task IdenticalMetadataNamesFromAliasedAssembliesCanBeSelectedExactly()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var alphaName = "SameFqnAlpha_" + suffix;
        var betaName = "SameFqnBeta_" + suffix;
        var alpha = await Generate(SameFqnProducer("value + 1"), assemblyName: alphaName);
        var beta = await Generate(SameFqnProducer("value + 2"), assemblyName: betaName);
        await NoErrors(alpha);
        await NoErrors(beta);
        var alphaImage = Emit(alpha);
        var betaImage = Emit(beta);
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(alphaImage));
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(betaImage));
        var alphaReference = MetadataReference.CreateFromImage(alphaImage,
            MetadataReferenceProperties.Assembly.WithAliases(["SharedRef", "AlphaRef"]));
        var betaReference = MetadataReference.CreateFromImage(betaImage,
            MetadataReferenceProperties.Assembly.WithAliases(["SharedRef", "BetaRef"]));

        var consumer = await GenerateWithReferences($$"""
            public static partial class Consumer
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value)
                {
                    var first = {{alphaName}}_Shared_Transform_ApplyExtensions.Apply(steps, value);
                    var second = {{betaName}}_Shared_Transform_ApplyExtensions.Apply(steps, value);
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
            """, "SameFqnConsumer_" + suffix, alphaReference, betaReference);

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
        var suffix = Guid.NewGuid().ToString("N");
        var alphaName = "UnaliasedAlpha_" + suffix;
        var betaName = "UnaliasedBeta_" + suffix;
        var alpha = await Generate(SameFqnProducer("value + 1"), assemblyName: alphaName);
        var beta = await Generate(SameFqnProducer("value + 2"), assemblyName: betaName);
        await NoErrors(alpha);
        await NoErrors(beta);
        var consumer = await GenerateWithReferences($$"""
            public static partial class Consumer
            {
                public static void Configuration(StepGraph steps, int value)
                {
                    var first = {{alphaName}}_Shared_Transform_ApplyExtensions.Apply(steps, value);
                    var second = {{betaName}}_Shared_Transform_ApplyExtensions.Apply(steps, value);
                }
            }
            """, "UnaliasedConsumer", MetadataReference.CreateFromImage(Emit(alpha)),
            MetadataReference.CreateFromImage(Emit(beta)));

        await Assert.That(consumer.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP019" && diagnostic.GetMessage().Contains(
                "extern alias", StringComparison.Ordinal))).IsTrue();

        var sharedAlias = MetadataReferenceProperties.Assembly.WithAliases(["SharedRef"]);
        var sharedConsumer = await GenerateWithReferences($$"""
            public static partial class SharedAliasConsumer
            {
                public static void Configuration(StepGraph steps, int value)
                {
                    var first = {{alphaName}}_Shared_Transform_ApplyExtensions.Apply(steps, value);
                    var second = {{betaName}}_Shared_Transform_ApplyExtensions.Apply(steps, value);
                }
            }
            """, "SharedAliasConsumer_" + suffix,
            MetadataReference.CreateFromImage(Emit(alpha), sharedAlias),
            MetadataReference.CreateFromImage(Emit(beta), sharedAlias));
        await Assert.That(sharedConsumer.Diagnostics.Any(diagnostic =>
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
                    var output = steps.Apply(value);
                }
            }
            """, "AmbiguousConsumer", MetadataReference.CreateFromImage(Emit(alpha)),
            MetadataReference.CreateFromImage(Emit(beta)));

        await Assert.That(consumer.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP009" && diagnostic.GetMessage().Contains(
                "ambiguous", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task MultipleCompositeFunctionsInOneOwnerGenerateIndependentFactoriesAndPipelines()
    {
        var local = await Generate("""
            internal static class NumberSteps
            {
                [Step]
                internal static int Increment(int value, CancellationToken token = default) => value + 1;

                [Step]
                internal static int Double(int value, CancellationToken token = default) => value * 2;
            }

            public static partial class Operations
            {
                [Pipeline]
                public static void Import(StepGraph steps, int value)
                {
                    var output = steps.Increment(value);
                }

                [Pipeline]
                public static void Export(StepGraph steps, int value)
                {
                    var output = steps.Double(value);
                }

                [Pipeline]
                public static void A_B(StepGraph steps, int value)
                {
                    var c = steps.Increment(value);
                }

                [Pipeline]
                public static void A(StepGraph steps, int value)
                {
                    var b_C = steps.Increment(value);
                }
            }

            public static partial class Root
            {
                [Pipeline]
                public static void Run(StepGraph steps, int value)
                {
                    var imported = steps.Import(value);
                    var exported = steps.Export(value);
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var imported = new Operations.ImportPipeline().Execute(3);
                    var exported = new Operations.ExportPipeline().Execute(3);
                    var underscoredFunction = new Operations.A_BPipeline().Execute(3);
                    var underscoredNode = new Operations.APipeline().Execute(3);
                    var combined = new Root.RunPipeline().Execute(3);
                    return Task.FromResult($"{imported.Output}:{exported.Output}:" +
                        $"{combined.Imported.Output}:{combined.Exported.Output}:" +
                        $"{underscoredFunction.C}:{underscoredNode.B_C}");
                }
            }
            """);
        await NoErrors(local);
        await Assert.That(local.GeneratedSource).Contains("readonly struct ImportResult");
        await Assert.That(local.GeneratedSource).Contains("readonly struct ExportResult");
        await Assert.That(local.GeneratedSource).Contains("sealed class ImportPipeline");
        await Assert.That(local.GeneratedSource).Contains("sealed class ExportPipeline");
        await Assert.That(local.GeneratedSource).Contains("StepBuilder<global::Operations.ImportResult> Import(");
        await Assert.That(local.GeneratedSource).Contains("StepBuilder<global::Operations.ExportResult> Export(");
        var localAssembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(local)));
        var run = localAssembly.GetType("Scenario")!.GetMethod("Run")!
            .CreateDelegate<Func<Task<string>>>();
        await Assert.That(await run()).IsEqualTo("4:6:4:6:4:4");

        var producer = await Generate("""
            internal static class RemoteNumberSteps
            {
                [Step]
                internal static int Increment(int value, CancellationToken token = default) => value + 1;

                [Step]
                internal static int Double(int value, CancellationToken token = default) => value * 2;
            }

            public static partial class RemoteOperations
            {
                public static void Import(StepGraph steps, int value)
                {
                    var output = steps.Increment(value);
                }

                public static void Export(StepGraph steps, int value)
                {
                    var output = steps.Double(value);
                }
            }
            """, assemblyName: "MultipleCompositeProducer");
        await NoErrors(producer);
        var producerImage = Emit(producer);
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(producerImage));
        var consumer = await Generate("""
            public static partial class RemoteRoot
            {
                [Pipeline]
                public static void Run(StepGraph steps, int value)
                {
                    var imported = steps.Import(value);
                    var exported = steps.Export(value);
                }
            }

            public static class RemoteScenario
            {
                public static Task<string> Run()
                {
                    var result = new RemoteRoot.RunPipeline().Execute(5);
                    return Task.FromResult($"{result.Imported.Output}:{result.Exported.Output}");
                }
            }
            """, MetadataReference.CreateFromImage(producerImage), "MultipleCompositeConsumer");
        await NoErrors(consumer);
        var consumerAssembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(consumer)));
        var remoteRun = consumerAssembly.GetType("RemoteScenario")!.GetMethod("Run")!
            .CreateDelegate<Func<Task<string>>>();
        await Assert.That(await remoteRun()).IsEqualTo("6:10");

        var mixedReference = CompilePlainReference("""
            public static class MixedProtocol
            {
                public readonly struct GoodResult { }
                public static void Good(StepGraph steps, int value) { }
                public static GoodResult Good(string path, int value, CancellationToken token = default) => default;

                public readonly struct BadResult { }
                public static void Bad(StepGraph steps, int value) { }
            }
            """, "MixedCompositeProtocol");
        var mixedConsumer = await Generate("""
            public static partial class MixedConsumer
            {
                public static void Run(StepGraph steps, int value)
                {
                    var good = steps.Good(value);
                    var bad = steps.Bad(value);
                }
            }
            """, mixedReference, "MixedCompositeConsumer");
        await Assert.That(mixedConsumer.GeneratedSource).Contains(
            "StepBuilder<global::MixedProtocol.GoodResult> Good(");
        await Assert.That(mixedConsumer.GeneratedSource).DoesNotContain(
            "StepBuilder<global::MixedProtocol.BadResult> Bad(");
        await Assert.That(mixedConsumer.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP019" && diagnostic.GetMessage().Contains(
                "MixedProtocol.Bad", StringComparison.Ordinal))).IsTrue();

        var overloaded = await Generate("""
            public static partial class Overloaded
            {
                public static void Build(StepGraph steps, int value) { }
                public static void Build(StepGraph steps, string value) { }
            }
            """);
        await Assert.That(overloaded.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP009" && diagnostic.GetMessage().Contains(
                "reserved for generated execution", StringComparison.Ordinal))).IsTrue();
        await Assert.That(overloaded.GeneratedSource).DoesNotContain("partial class Overloaded");

        var duplicateFacade = await Generate("""
            public static partial class DuplicateFacade
            {
                [Pipeline(Name = "Same")]
                public static void First(StepGraph steps) { }

                [Pipeline(Name = "Same")]
                public static void Second(StepGraph steps) { }
            }
            """);
        await Assert.That(duplicateFacade.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP018")).IsTrue();
    }

    [Test]
    public async Task FunctionIdentifiedCompositeFactoriesRemainDeterministicAcrossOwnersAndAssemblies()
    {
        await SameNamedCompositesBindByExactGeneratedFactorySymbol();
        await IdenticalMetadataNamesFromAliasedAssembliesCanBeSelectedExactly();
        await IdenticalMetadataNamesWithoutAliasesReportTtp019();
        await AmbiguousUnqualifiedFactoryReportsActionableStaticGraphDiagnostic();
        await ExplicitCarrierSelectionIgnoresUnselectedMalformedProtocols();
    }

    private async Task ExplicitCarrierSelectionIgnoresUnselectedMalformedProtocols()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var qualifiedName = "QualifiedGood_" + suffix;
        var qualifiedProducer = await Generate("""
            namespace Alpha
            {
                internal static class Values
                {
                    [Step]
                    internal static int Increment(int value, CancellationToken token = default) => value + 1;
                }

                public static partial class GoodOwner
                {
                    public static void Apply(StepGraph steps, int value)
                    {
                        var output = steps.Increment(value);
                    }
                }
            }
            """, assemblyName: qualifiedName);
        await NoErrors(qualifiedProducer);
        var qualifiedImage = Emit(qualifiedProducer);
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(qualifiedImage));
        var malformedOwner = CompilePlainReference("""
            namespace Beta
            {
                public static class BadOwner
                {
                    public static void Apply(StepGraph steps, int value) { }
                }
            }
            """, "QualifiedBad_" + suffix);
        var qualifiedConsumer = await GenerateWithReferences("""
            public static partial class QualifiedRoot
            {
                [Pipeline]
                public static void Run(StepGraph steps, int value)
                {
                    var selected = GoodOwner_ApplyExtensions.Apply(steps, value);
                }
            }

            public static class QualifiedScenario
            {
                public static Task<string> Run()
                {
                    var result = new QualifiedRoot.RunPipeline().Execute(1);
                    return Task.FromResult(result.Selected.Output.ToString());
                }
            }
            """, "QualifiedConsumer_" + suffix,
            MetadataReference.CreateFromImage(qualifiedImage), malformedOwner);
        await NoErrors(qualifiedConsumer);
        await Assert.That(qualifiedConsumer.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP019")).IsFalse();
        await Assert.That(qualifiedConsumer.GeneratedSource).DoesNotContain("global::Beta.BadOwner.Apply(");
        var qualifiedAssembly = AssemblyLoadContext.Default.LoadFromStream(
            new MemoryStream(Emit(qualifiedConsumer)));
        var qualifiedRun = qualifiedAssembly.GetType("QualifiedScenario")!.GetMethod("Run")!
            .CreateDelegate<Func<Task<string>>>();
        await Assert.That(await qualifiedRun()).IsEqualTo("2");

        var mixedConsumer = await Generate($$"""
            public static partial class MixedRoot
            {
                [Pipeline]
                public static void Run(StepGraph steps, int value)
                {
                    var direct = steps.Apply(value);
                    var basic = GoodOwner_ApplyExtensions.Apply(steps, value);
                    var namespaced = Alpha_GoodOwner_ApplyExtensions.Apply(steps, value);
                    var assembly = {{qualifiedName}}_Alpha_GoodOwner_ApplyExtensions.Apply(steps, value);
                }
            }

            public static class MixedScenario
            {
                public static Task<string> Run()
                {
                    var result = new MixedRoot.RunPipeline().Execute(1);
                    return Task.FromResult($"{result.Direct.Output}:{result.Basic.Output}:" +
                        $"{result.Namespaced.Output}:{result.Assembly.Output}");
                }
            }
            """, MetadataReference.CreateFromImage(qualifiedImage),
            "MixedCarrierConsumer_" + suffix);
        await NoErrors(mixedConsumer);
        var mixedAssembly = AssemblyLoadContext.Default.LoadFromStream(
            new MemoryStream(Emit(mixedConsumer)));
        var mixedRun = mixedAssembly.GetType("MixedScenario")!.GetMethod("Run")!
            .CreateDelegate<Func<Task<string>>>();
        await Assert.That(await mixedRun()).IsEqualTo("2:2:2:2");

        var aliasGoodName = "AliasGood_" + suffix;
        var aliasBadName = "AliasBad_" + suffix;
        var aliasProducer = await Generate(SameFqnProducer("value + 1"), assemblyName: aliasGoodName);
        await NoErrors(aliasProducer);
        var aliasImage = Emit(aliasProducer);
        AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(aliasImage));
        var aliasBad = CompilePlainReference("""
            namespace Shared
            {
                public static class Transform
                {
                    public static void Apply(StepGraph steps, int value) { }
                }
            }
            """, aliasBadName);
        var aliasGoodReference = MetadataReference.CreateFromImage(aliasImage,
            MetadataReferenceProperties.Assembly.WithAliases(["SharedRef", "GoodRef"]));
        var aliasBadReference = aliasBad.WithProperties(
            MetadataReferenceProperties.Assembly.WithAliases(["SharedRef", "BadRef"]));
        var aliasConsumer = await GenerateWithReferences($$"""
            public static partial class AliasRoot
            {
                [Pipeline]
                public static void Run(StepGraph steps, int value)
                {
                    var selected = {{aliasGoodName}}_Shared_Transform_ApplyExtensions.Apply(steps, value);
                }
            }

            public static class AliasScenario
            {
                public static Task<string> Run()
                {
                    var result = new AliasRoot.RunPipeline().Execute(1);
                    return Task.FromResult(result.Selected.Output.ToString());
                }
            }
            """, "AliasConsumer_" + suffix, aliasGoodReference, aliasBadReference);
        await NoErrors(aliasConsumer);
        await Assert.That(aliasConsumer.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP019")).IsFalse();
        await Assert.That(aliasConsumer.GeneratedSource).Contains("extern alias GoodRef;");
        await Assert.That(aliasConsumer.GeneratedSource).DoesNotContain("extern alias BadRef;");
        var aliasAssembly = AssemblyLoadContext.Default.LoadFromStream(
            new MemoryStream(Emit(aliasConsumer)));
        var aliasRun = aliasAssembly.GetType("AliasScenario")!.GetMethod("Run")!
            .CreateDelegate<Func<Task<string>>>();
        await Assert.That(await aliasRun()).IsEqualTo("2");
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

        AssertSummaryBefore(generated.GeneratedSource, "public static ConfigurationResult Configuration");
        await Assert.That(generated.GeneratedSource).DoesNotContain("__TedToolkitCompositeStepState");
        await Assert.That(generated.GeneratedSource).DoesNotContain("__TedToolkitPrepareCompositeStep");
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
        await Assert.That(publicLeafPipeline.GetMembers("ExecuteWithoutResults").Length).IsEqualTo(0);
        await Assert.That(publicLeaf.GetTypeMembers("UnmarkedPipeline").Length).IsEqualTo(0);

        var internalLeafPipeline = generated.Compilation.GetTypeByMetadataName("InternalLeafSteps")!
            .GetTypeMembers("LoadPipeline").Single();
        await Assert.That(internalLeafPipeline.DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(internalLeafPipeline.GetMembers("ExecuteAsync").Length).IsEqualTo(1);
        await Assert.That(internalLeafPipeline.GetMembers("ExecuteWithoutResultsAsync").Length).IsEqualTo(0);

        var publicCompositePipeline = generated.Compilation.GetTypeByMetadataName("PublicComposite")!
            .GetTypeMembers("ImportPipeline").Single();
        await Assert.That(publicCompositePipeline.DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(publicCompositePipeline.GetMembers("Execute").Length).IsEqualTo(1);
        await Assert.That(publicCompositePipeline.GetMembers("ExecuteWithoutResults").Length).IsEqualTo(0);

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
    public async Task NamedCompositeGeneratedMemberCollisionsAreRejected()
    {
        foreach (var reservedMember in new[]
        {
            "public readonly struct ConfigurationResult { }",
            "public static int ConfigurationResult;",
            "public static int ConfigurationResult { get; set; }",
            "public static void Configuration(int value) { }",
        })
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
            await Assert.That(diagnostics.Length > 0).IsTrue().Because(reservedMember);
            await Assert.That(diagnostics.All(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains("reserved for generated execution",
                    StringComparison.Ordinal))).IsTrue().Because(reservedMember);
            await Assert.That(generated.GeneratedSource).DoesNotContain(
                "partial class ReservedComposite").Because(reservedMember);
            await Assert.That(generated.GeneratedSource).DoesNotContain(
                "sealed class ConfigurationPipeline").Because(reservedMember);
        }
    }

    [Test]
    public async Task StructuralCompositeProtocolExecutesAcrossAssemblies()
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
                public static void Run(StepGraph steps, int value)
                {
                    var external = steps.Configuration(value)
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
                    var pipeline = new Consumer.RunPipeline(services);
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

    private static string StructuralProtocolSource(
        string execution,
        string result = "public readonly struct BuildResult",
        string declaration = "public static void Build(StepGraph steps, int value) { }") => $$"""
        public static class External
        {
            {{result}} { }
            {{declaration}}
            {{execution}}
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
                public static void Apply(StepGraph steps, int value)
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
                public static void Apply(StepGraph steps, int value)
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
