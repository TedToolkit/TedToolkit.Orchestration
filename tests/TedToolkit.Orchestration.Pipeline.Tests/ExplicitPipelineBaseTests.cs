namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task UnattributedConfigurationIsIgnored()
    {
        var generated = await Generate("""
            public readonly ref partial struct Example
            {
                private void Configuration(StepGraph graph) { }
            }
            """);
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        await Assert.That(owner.GetMembers("Execute").Length).IsEqualTo(0);
        await Assert.That(owner.GetTypeMembers("Pipeline").Length).IsEqualTo(0);
    }

    [Test]
    public async Task CompositeAttributeOwnsGenerationWithoutInheritance()
    {
        var generated = await Generate(NamedSteps + """
            [CompositeStep]
            public readonly ref partial struct Example
            {
                private void Configuration(StepGraph graph) { var sum = graph.Add(40, 2); }
            }
            """);
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        await Assert.That(owner.GetMembers("Execute").Length).IsEqualTo(1);
        await Assert.That(owner.BaseType!.SpecialType).IsEqualTo(Microsoft.CodeAnalysis.SpecialType.System_ValueType);
    }
}
