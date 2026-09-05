using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments("")]
    [Arguments(": Unrelated.Pipeline")]
    public async Task ConfigurationWithoutRuntimePipelineInheritanceIsIgnored(string baseList)
    {
        var generated = await Generate(NamedSteps + """
            namespace Unrelated { public class Pipeline {} }
            public partial class Example BASE
            {
                private void Configure(global::TedToolkit.Orchestration.Pipeline.Pipeline.Builder p) { p.Add(1, 2); }
            }
            """.Replace("BASE", baseList));
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        await Assert.That(owner.GetMembers("Execute").Length).IsEqualTo(0);
        await Assert.That(owner.GetMembers("Results").Length).IsEqualTo(0);
        await Assert.That(owner.InstanceConstructors.All(constructor => constructor.IsImplicitlyDeclared)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UserOwnsTheBaseDeclarationIncludingIndirectInheritance(bool indirect)
    {
        var generated = await Generate("using Root = TedToolkit.Orchestration.Pipeline.Pipeline;" + NamedSteps + """
            public abstract class Intermediate : Root {}
            public sealed partial class Example : BASE {}
            public sealed partial class Example
            {
                protected override void Configuration(Builder p) { var sum = p.Add(40, 2); }
            }
            """.Replace("BASE", indirect ? "Intermediate" : "Root"));
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        await Assert.That(owner.GetMembers("Execute").Length).IsEqualTo(1);
        var declaration = (ClassDeclarationSyntax)owner.GetMembers("Execute").OfType<IMethodSymbol>().Single()
            .DeclaringSyntaxReferences.Single().GetSyntax().Parent!;
        await Assert.That(declaration.BaseList is null).IsTrue();
        await Assert.That(owner.BaseType!.Name).IsEqualTo(indirect ? "Intermediate" : "Pipeline");
    }
}
