namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task UnattributedConfigurationGeneratesCompositeWithoutPipelineFacade()
    {
        var generated = await Generate("""
            public static partial class Example
            {
                public static void Configuration(StepGraph graph) { }
            }
            """);
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        await Assert.That(owner.GetMembers("Execute").Length).IsEqualTo(0);
        await Assert.That(owner.GetTypeMembers("ConfigurationPipeline").Length).IsEqualTo(0);
        await Assert.That(owner.GetMembers("Configuration").Length).IsEqualTo(2);
    }

    [Test]
    public async Task ConfigurationOwnsGenerationWithoutInheritance()
    {
        var generated = await Generate(NamedSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph graph) { var sum = graph.Add(40, 2); }
            }
            """);
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        await Assert.That(owner.GetMembers("Configuration").Length).IsEqualTo(2);
        await Assert.That(owner.BaseType!.SpecialType).IsEqualTo(Microsoft.CodeAnalysis.SpecialType.System_Object);
    }
}
