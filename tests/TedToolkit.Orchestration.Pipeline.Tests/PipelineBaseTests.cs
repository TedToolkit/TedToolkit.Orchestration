namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task RemovedRuntimeContractsAreNotPresent()
    {
        var runtime = typeof(StepGraph).Assembly;
        await Assert.That(runtime.GetType("TedToolkit.Orchestration.Pipeline.ConfigurationPipeline")).IsNull();
        await Assert.That(runtime.GetType("TedToolkit.Orchestration.Pipeline.StepMetadata")).IsNull();
        await Assert.That(runtime.GetType(
            "TedToolkit.Orchestration.Pipeline.Attributes.StepPolicyAttribute")).IsNull();
        await Assert.That(runtime.GetType(
            "TedToolkit.Orchestration.Pipeline.CompilerServices.PipelineExecutionSupport")!
            .GetMethod("CancelRemainingAsync", System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static)).IsNull();
    }

    [Test]
    public async Task UnrelatedPartialStructDoesNotReceiveTheCs0282Suppression()
    {
        var generated = await Generate("""
            internal readonly ref partial struct Unrelated
            {
                private readonly int first;
            }
            internal readonly ref partial struct Unrelated
            {
                private readonly int second;
            }
            """);

        await Assert.That(generated.Diagnostics.Any(item => item.Id == "CS0282")).IsTrue();
    }

    [Test]
    public async Task MultipleCompositesKeepTheirInputsSeparate()
    {
        var result = await Run(NamedSteps + """
            public static partial class First
            {
                [Pipeline]
                public static void Configuration(StepGraph graph, int value) { var sum = graph.Add(value, 2); }
            }
            public static partial class Second
            {
                [Pipeline]
                public static void Configuration(StepGraph graph, int value) { var sum = graph.Add(40, value); }
            }
            """ + AsyncScenario("""
                var first = new First.ConfigurationPipeline();
                var second = new Second.ConfigurationPipeline();
                return $"{first.Execute(10).Sum}:{second.Execute(2).Sum}:{typeof(StepGraph).IsValueType}";
                """));
        await Assert.That(result).IsEqualTo("12:42:True");
    }

    [Test]
    [Arguments("public partial class Example { public static void Configuration(StepGraph graph) { } }")]
    [Arguments("public static class Example { public static void Configuration(StepGraph graph) { } }")]
    [Arguments("public static partial class Example<T> { public static void Configuration(StepGraph graph) { } }")]
    [Arguments("public static partial class Example { public void Configuration(StepGraph graph) { } }")]
    [Arguments("public static partial class Example { private static void Configuration(StepGraph graph) { } }")]
    public async Task InvalidCompositeDeclarationsAreRejected(string declaration)
    {
        var generated = await Generate(declaration);
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    public async Task ArgumentExpressionsAreEvaluatedForEachInvocation()
    {
        var result = await Run(NamedSteps + """
            public static partial class Example
            {
                public static int Evaluations;
                private static int Next(int value) { Evaluations++; return value; }
                [Pipeline]
                public static void Configuration(StepGraph graph, int value)
                {
                    var sum = graph.Add(Next(value), 2);
                }
            }
            """ + AsyncScenario("""
                var pipeline = new Example.ConfigurationPipeline();
                var first = pipeline.Execute(10);
                pipeline.Execute(20);
                return $"{first.Sum}:{Example.Evaluations}";
                """));
        await Assert.That(result).IsEqualTo("12:2");
    }

    [Test]
    public async Task CustomExtensionsCannotSubstituteGeneratedFactories()
    {
        var generated = await Generate(NamedSteps + """
            public static class Custom
            {
                public static StepBuilder<int> Add(this StepGraph graph, int a, int b) => default;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph graph) { var sum = graph.Add(40, 2); }
            }
            """);
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    public async Task GeneratedExecutionContainsNoRuntimeGraphOrReflectionDispatch()
    {
        var generated = await Generate(NamedSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph graph) { var sum = graph.Add(40, 2); }
            }
            """);
        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource.Contains(
            "global::AddStepMethods.Add(40, 2, executionToken)")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("GetType(") ||
            generated.GeneratedSource.Contains("Invoke(") ||
            generated.GeneratedSource.Contains("BeginNode")).IsFalse();
    }
}
