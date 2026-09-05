using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SerialEntrypointsMatchStepKindsAndHaveNoExceptionOrLocalFunctionWrappers(bool asynchronous)
    {
        var step = asynchronous
            ? "internal readonly ref struct Work : IAsyncStep<int> { public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(42); }"
            : "internal readonly ref struct Work : IStep<int> { public int Execute(CancellationToken token) => 42; }";
        var generated = await Generate(step + """
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { var work = p.Work(); }
            }
            """);
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        foreach (var name in new[] { "Execute", "ExecuteWithoutResults" })
        {
            var entry = owner.GetMembers(name + (asynchronous ? "Async" : "")).OfType<IMethodSymbol>().Single();
            await Assert.That(entry.IsAsync).IsEqualTo(asynchronous);
            await Assert.That(owner.GetMembers(name + (asynchronous ? "" : "Async")).Length).IsEqualTo(0);
            var syntax = (MethodDeclarationSyntax)await entry.DeclaringSyntaxReferences.Single().GetSyntaxAsync();
            await Assert.That(syntax.DescendantNodes().Any(node => node is TryStatementSyntax or LocalFunctionStatementSyntax)).IsFalse();
            if (!asynchronous)
                await Assert.That(entry.ReturnType.Name).IsEqualTo(name == "Execute" ? "Results" : "Void");
        }
        await Assert.That(owner.GetTypeMembers().Any(type => type.Name.EndsWith("Constructor"))).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains("IStepConstructor") || generated.GeneratedSource.Contains("IAsyncStepConstructor")).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRetriesReconstructStepsAfterConstructorFailureWithoutResolvingServicesAgain(bool discard)
    {
        var result = await Run("""
            public sealed class State { public int Constructions; public int Resolutions; public int Executions; }
            public sealed class Services(State state) : IServiceProvider
            {
                public object? GetService(Type type) { state.Resolutions++; return state; }
            }
            [StepPolicy(RetryCount = 1)]
            internal readonly ref struct Work : IAsyncStep<int>
            {
                private readonly State state;
                public Work([FromServices] State state)
                {
                    this.state = state;
                    if (++state.Constructions == 1) throw new InvalidOperationException("constructor");
                }
                public Task<int> ExecuteAsync(CancellationToken token) { state.Executions++; return Task.FromResult(42); }
            }
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p) { p.Work(); }
            }
            """ + AsyncScenario("""
                var state = new State();
                await new Example(new Services(state)).METHOD();
                return $"{state.Constructions}:{state.Resolutions}:{state.Executions}";
                """.Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")));
        await Assert.That(result).IsEqualTo("2:1:1");
    }

    [Test]
    [Arguments("Execute")]
    [Arguments("ExecuteWithoutResults")]
    public async Task SynchronousEntryNameCollisionsHaveAConfigurationDiagnostic(string name)
    {
        var generated = await Generate(NamedSteps + """
            public sealed partial class Example : Pipeline
            {
                public void METHOD() {}
                protected override void Configuration(Builder p) { p.Add(1, 2); }
            }
            """.Replace("METHOD", name));
        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "TTP009")).IsTrue();
    }
}
