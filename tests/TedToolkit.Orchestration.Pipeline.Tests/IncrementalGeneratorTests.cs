using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TedToolkit.Orchestration.Pipeline.Analyzer;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task UnrelatedEditsKeepGeneratedSourcesStable()
    {
        var first = CSharpSyntaxTree.ParseText(Imports + """
            internal static class FirstStepMethods
            {
                [Step]
                internal static int FirstStep(int value, CancellationToken token) => value;
            }

            public static partial class FirstComposite
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var first = steps.FirstStep(1);
                }
            }
            """, ParseOptions, "First.cs");
        var second = CSharpSyntaxTree.ParseText(Imports + """
            internal static class SecondStepMethods
            {
                [Step]
                internal static int SecondStep(int value, CancellationToken token) => value;
            }

            public static partial class SecondComposite
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var second = steps.SecondStep(2);
                }
            }
            """, ParseOptions, "Second.cs");
        var unrelated = CSharpSyntaxTree.ParseText(
            "internal static class Unrelated { internal const int Value = 1; }",
            ParseOptions, "Unrelated.cs");
        var compilation = CSharpCompilation.Create(
            "IncrementalPipelineScenario",
            [first, second, unrelated],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PipelineExecutorGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var changed = compilation.ReplaceSyntaxTree(unrelated, CSharpSyntaxTree.ParseText(
            "internal static class Unrelated { internal const int Value = 2; }",
            ParseOptions, "Unrelated.cs"));
        driver = driver.RunGenerators(changed);

        var result = driver.GetRunResult().Results.Single();
        await Assert.That(result.TrackedSteps.Values
            .SelectMany(runs => runs)
            .SelectMany(run => run.Outputs)
            .Any(output => output.Reason == IncrementalStepRunReason.Cached)).IsTrue();
        await Assert.That(result.GeneratedSources
            .Select(source => source.HintName)
            .Distinct(StringComparer.Ordinal).Count()).IsEqualTo(result.GeneratedSources.Length);
    }

}
