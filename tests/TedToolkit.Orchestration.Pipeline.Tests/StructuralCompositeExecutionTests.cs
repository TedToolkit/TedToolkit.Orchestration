using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.Loader;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    private static MetadataReference CompilePlainReference(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(Imports + source, ParseOptions, "Protocol.cs")],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    [Test]
    public async Task NamedCompositeGeneratesSingleExecutionOverload()
    {
        var generated = await Generate("""
            internal static class Values
            {
                [Step]
                internal static int Sync(int value, CancellationToken token = default) => value;

                [Step]
                internal static Task<int> Async(int value, CancellationToken token = default) =>
                    Task.FromResult(value);
            }

            public static partial class SyncFlow
            {
                public static void Import(StepGraph steps, int value)
                {
                    var output = steps.Sync(value);
                }
            }

            public static partial class AsyncFlow
            {
                public static void Import(StepGraph steps, int value)
                {
                    var output = steps.Async(value);
                }
            }
            """);

        await NoErrors(generated);
        foreach (var ownerName in new[] { "SyncFlow", "AsyncFlow" })
        {
            var owner = generated.Compilation.GetTypeByMetadataName(ownerName)!;
            await Assert.That(owner.GetTypeMembers("ImportResult").Length).IsEqualTo(1);
            await Assert.That(owner.GetTypeMembers("Results").Length).IsEqualTo(0);
            await Assert.That(owner.GetMembers("Import").OfType<IMethodSymbol>().Count()).IsEqualTo(2);
        }

        await Assert.That(generated.GeneratedSource).DoesNotContain("__TedToolkitExecuteCompositeStep");
        await Assert.That(generated.GeneratedSource).DoesNotContain("__TedToolkitCompositeStepState");
        await Assert.That(generated.GeneratedSource).DoesNotContain("__TedToolkitPrepareCompositeStep");
        await Assert.That(generated.GeneratedSource).DoesNotContain("GeneratedCompositeStepAttribute");
        await Assert.That(typeof(StepGraph).Assembly.GetType(
            "TedToolkit.Orchestration.Pipeline.CompilerServices.GeneratedCompositeStepAttribute"))
            .IsNull();
    }

    [Test]
    public async Task PipelinesExposeSingleNaturalReturnShape()
    {
        var generated = await Generate("""
            public static partial class SyncValue
            {
                [Step, Pipeline]
                public static int Value(CancellationToken token = default) => 1;
            }

            public static partial class AsyncValue
            {
                [Step, Pipeline]
                public static Task<int> Load(CancellationToken token = default) => Task.FromResult(2);
            }

            public static partial class SyncEffect
            {
                [Step, Pipeline]
                public static void Apply(CancellationToken token = default) { }
            }

            public static partial class AsyncEffect
            {
                [Step, Pipeline]
                public static Task Send(CancellationToken token = default) => Task.CompletedTask;
            }

            public static partial class SyncFlow
            {
                [Pipeline]
                public static void Import(StepGraph steps)
                {
                    var ignored = steps.Apply();
                    var value = steps.Value().DependsOn(ignored);
                }
            }

            public static partial class AsyncFlow
            {
                [Pipeline]
                public static void Import(StepGraph steps)
                {
                    var ignored = steps.Send();
                    var value = steps.Load().DependsOn(ignored);
                }
            }

            public static class ResultScenario
            {
                public static async Task<string> Run()
                {
                    var syncValue = new SyncValue.ValuePipeline().Execute();
                    var asyncValue = await new AsyncValue.LoadPipeline().ExecuteAsync();
                    new SyncEffect.ApplyPipeline().Execute();
                    await new AsyncEffect.SendPipeline().ExecuteAsync();
                    var syncComposite = new SyncFlow.ImportPipeline().Execute();
                    var asyncComposite = await new AsyncFlow.ImportPipeline().ExecuteAsync();
                    return $"{syncValue}:{asyncValue}:{syncComposite.Value}:{asyncComposite.Value}";
                }
            }
            """);

        await NoErrors(generated);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(generated)));
        var run = assembly.GetType("ResultScenario")!.GetMethod("Run")!
            .CreateDelegate<Func<Task<string>>>();
        await Assert.That(await run()).IsEqualTo("1:2:1:2");
        AssertReturn(generated.Compilation, "SyncValue+ValuePipeline", "Execute", "int");
        AssertReturn(generated.Compilation, "AsyncValue+LoadPipeline", "ExecuteAsync",
            "System.Threading.Tasks.Task<int>");
        AssertReturn(generated.Compilation, "SyncEffect+ApplyPipeline", "Execute",
            "void");
        AssertReturn(generated.Compilation, "AsyncEffect+SendPipeline", "ExecuteAsync",
            "System.Threading.Tasks.Task");
        AssertReturn(generated.Compilation, "SyncFlow+ImportPipeline", "Execute",
            "SyncFlow.ImportResult");
        AssertReturn(generated.Compilation, "AsyncFlow+ImportPipeline", "ExecuteAsync",
            "System.Threading.Tasks.Task<AsyncFlow.ImportResult>");
        AssertVoidFactory(generated.Compilation, "SyncEffect_ApplyExtensions", "Apply");
        AssertVoidFactory(generated.Compilation, "AsyncEffect_SendExtensions", "Send");
        foreach (var ownerName in new[] { "SyncFlow", "AsyncFlow" })
        {
            var result = generated.Compilation.GetTypeByMetadataName(ownerName)!
                .GetTypeMembers("ImportResult").Single();
            var properties = result.GetMembers().OfType<IPropertySymbol>().ToArray();
            await Assert.That(properties.Length).IsEqualTo(1);
            await Assert.That(properties[0].Name).IsEqualTo("Value");
        }

        await Assert.That(generated.Compilation.GetTypeByMetadataName(
            "TedToolkit.Orchestration.Pipeline.Unit")).IsNull();
        await Assert.That(generated.GeneratedSource).DoesNotContain("ExecuteWithoutResults");
    }

    [Test]
    public async Task SingleCompositeExecutionPreservesRuntimeSemantics()
    {
        await NestedStepLoggerUsesEveryCompositeDisplaySegment();
        await ConfigurationTimeoutAppliesFromNodeModifier();
        await NestedCompositePreservesFailureAndDrainsStartedSibling();
        await CallerCancellationFlowsThroughNestedCompositeAndWaitsForCleanup();
        await ServicesResolveExactKeysFromTheCallerScope(discardResults: false);
    }

    private static void AssertReturn(
        Compilation compilation, string metadataName, string methodName, string returnType)
    {
        var method = compilation.GetTypeByMetadataName(metadataName)!
            .GetMembers(methodName).OfType<IMethodSymbol>().Single();
        if (method.ReturnType.ToDisplayString() != returnType)
            throw new InvalidOperationException(
                $"{metadataName}.{methodName} returned {method.ReturnType}, expected {returnType}");
        if (method.ContainingType.GetMembers().OfType<IMethodSymbol>().Count(candidate =>
            candidate.Name is "Execute" or "ExecuteAsync") != 1)
            throw new InvalidOperationException($"{metadataName} must expose one execution method");
    }

    private static void AssertVoidFactory(
        Compilation compilation, string typeName, string methodName)
    {
        var factory = compilation.GetTypeByMetadataName(
            "TedToolkit.Orchestration.Pipeline." + typeName)!
            .GetMembers(methodName).OfType<IMethodSymbol>().Single();
        if (factory.ReturnType.ToDisplayString() !=
            "TedToolkit.Orchestration.Pipeline.StepBuilder")
            throw new InvalidOperationException(
                $"{typeName}.{methodName} must return non-generic StepBuilder");
    }
}
