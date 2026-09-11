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
            ? "internal static class WorkSteps { [Step] internal static Task<int> Work(CancellationToken token) => Task.FromResult(42); }"
            : "internal static class WorkSteps { [Step] internal static int Work(CancellationToken token) => 42; }";
        var generated = await Generate(step + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p) { var work = p.Work(); }
            }
            """);
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!
            .GetTypeMembers("ConfigurationPipeline").Single();
        foreach (var name in new[] { "Execute", "ExecuteWithoutResults" })
        {
            var entry = owner.GetMembers(name + (asynchronous ? "Async" : "")).OfType<IMethodSymbol>().Single();
            await Assert.That(entry.IsAsync).IsFalse();
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
    public async Task AsyncRetriesReinvokeAfterSetupFailureWithoutResolvingServicesAgain(bool discard)
    {
        var result = await Run("""
            public sealed class State { public int Constructions; public int Resolutions; public int Executions; }
            public sealed class Services(State state) : IServiceProvider
            {
                public object? GetService(Type type) { state.Resolutions++; return state; }
            }
            internal static class WorkStepMethods
            {
                [Step]
                internal static Task<int> Work([FromServices] State state, CancellationToken token)
                {
                    if (++state.Constructions == 1) throw new InvalidOperationException("construction");
                    state.Executions++;
                    return Task.FromResult(42);
                }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p) { p.Work().WithRetry(1); }
            }
            """ + AsyncScenario("""
                var state = new State();
                await new Example.ConfigurationPipeline(new Services(state)).METHOD();
                return $"{state.Constructions}:{state.Resolutions}:{state.Executions}";
                """.Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")));
        await Assert.That(result).IsEqualTo("2:1:1");
    }

    [Test]
    [Arguments("Execute")]
    [Arguments("ExecuteWithoutResults")]
    public async Task OuterMemberNamesDoNotCollideWithNestedPipelineEntrypoints(string name)
    {
        var generated = await Generate(NamedSteps + """
            public static partial class Example
            {
                public static void METHOD() {}
                [Pipeline]
                public static void Configuration(StepGraph p) { p.Add(1, 2); }
            }
            """.Replace("METHOD", name));
        await NoErrors(generated);
    }
}
