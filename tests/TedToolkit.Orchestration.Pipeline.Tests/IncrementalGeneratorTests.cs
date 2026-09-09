using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TedToolkit.Orchestration.Pipeline.Analyzer;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task UnrelatedEditsCacheStepContextsAndCompositeSources()
    {
        var first = CSharpSyntaxTree.ParseText(Imports + """
            internal readonly ref partial struct FirstStep(int value) : IStep<int>
            {
                public int Execute() => value;
            }

            [CompositeStep]
            public readonly ref partial struct FirstComposite
            {
                private void Configuration(StepGraph steps)
                {
                    var first = steps.FirstStep(1);
                }
            }
            """, ParseOptions, "First.cs");
        var second = CSharpSyntaxTree.ParseText(Imports + """
            internal readonly ref partial struct SecondStep(int value) : IStep<int>
            {
                public int Execute() => value;
            }

            [CompositeStep]
            public readonly ref partial struct SecondComposite
            {
                private void Configuration(StepGraph steps)
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
        await AssertCached(result, "StepContextSources");
        await AssertCached(result, "CompositeSources");
        await Assert.That(result.GeneratedSources
            .Select(source => source.HintName)
            .Distinct(StringComparer.Ordinal).Count()).IsEqualTo(result.GeneratedSources.Length);
    }

    private static async Task AssertCached(GeneratorRunResult result, string step)
    {
        await Assert.That(result.TrackedSteps.ContainsKey(step)).IsTrue();
        var reasons = result.TrackedSteps[step]
            .SelectMany(run => run.Outputs)
            .Select(output => output.Reason)
            .ToArray();
        await Assert.That(reasons.Length).IsGreaterThan(0);
        await Assert.That(reasons.All(
            reason => reason == IncrementalStepRunReason.Cached)).IsTrue();
    }
}
