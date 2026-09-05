using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ParallelEntrypointsDirectlyCallStepsWithoutBranchesOrNestedMethods()
    {
        var generated = await Generate(NamedSteps + ConcurrentSteps + """
            public sealed partial class Example : Pipeline
            {
                protected override void Configuration(Builder p)
                {
                    var root = p.Start();
                    var left = p.After(root);
                    var right = p.After(root);
                    p.Add(left, right);
                }
            }
            """);
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        var root = (MethodDeclarationSyntax)owner.GetMembers("RunRoot0Async").OfType<IMethodSymbol>().Single()
            .DeclaringSyntaxReferences.Single().GetSyntax();
        await Assert.That(root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Count(call => call.Expression.ToString() == "executionToken.ThrowIfCancellationRequested")).IsEqualTo(2);
        foreach (var name in new[] { "ExecuteAsync", "ExecuteWithoutResultsAsync" })
        {
            var syntax = (MethodDeclarationSyntax)owner.GetMembers(name).OfType<IMethodSymbol>().Single()
                .DeclaringSyntaxReferences.Single().GetSyntax();
            await Assert.That(syntax.DescendantNodes().Any(node => node is TryStatementSyntax or LocalFunctionStatementSyntax)).IsFalse();
            var waits = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(call => call.Expression.ToString().EndsWith("Task.WhenAll")).ToArray();
            await Assert.That(waits.Length).IsEqualTo(1);
            var waitMethod = (IMethodSymbol)generated.Compilation.GetSemanticModel(syntax.SyntaxTree).GetSymbolInfo(waits[0]).Symbol!;
            await Assert.That(waitMethod.IsGenericMethod).IsFalse();
            await Assert.That(syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Count(call => call.Expression.ToString().StartsWith("Run"))).IsEqualTo(4);
        }
        await Assert.That(generated.GeneratedSource.Contains("ParallelExecution") || generated.GeneratedSource.Contains("CompleteAsync")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains("RunBranch") || generated.GeneratedSource.Contains("branch0")).IsFalse();
    }

    [Test]
    [Arguments(false, false)] [Arguments(false, true)]
    [Arguments(true, false)] [Arguments(true, true)]
    public async Task StartupFailuresCancelAndDrainWithoutRunningDownstreamOrMaskingTheOriginal(bool argumentFailure, bool discard)
    {
        var result = await Run(ConcurrentSteps + """
            public sealed class State
            {
                public bool ArgumentFailure;
                public Exception Expected = new InvalidOperationException("original");
                public int Constructed;
                public int Downstream;
            }
            internal readonly ref struct Fail : IStep<int>
            {
                private readonly State state;
                public Fail(State state) { this.state = state; state.Constructed++; }
                public int Execute(CancellationToken token) => throw state.Expected;
            }
            internal readonly ref struct Touch(int value, State state) : IStep
            {
                public void Execute(CancellationToken token) => state.Downstream++;
            }
            public sealed partial class Example : Pipeline
            {
                private static State Read(State state) => state.ArgumentFailure ? throw state.Expected : state;
                private void Configure(Builder p, State state)
                {
                    var slow = p.Start();
                    var failed = p.Fail(Read(state));
                    p.Touch(failed, state);
                }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var state = new State { ArgumentFailure = ARGUMENT };
                var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var cleaned = 0;
                Task execution = new Example(services, state).METHOD(slowWork: async token =>
                {
                    using var registration = token.Register(() => { canceled.TrySetResult(); throw new Exception("callback"); });
                    try { await release.Task; return 1; }
                    finally { cleaned++; }
                });
                try
                {
                    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    if (execution.IsCompleted || cleaned != 0) return "returned before cleanup";
                }
                finally { release.TrySetResult(); }
                try { await execution; return "unexpected success"; }
                catch (Exception error)
                {
                    return $"{ReferenceEquals(error, state.Expected)}:{cleaned}:{state.Downstream}:{state.Constructed}";
                }
                """.Replace("ARGUMENT", argumentFailure ? "true" : "false").Replace("METHOD", discard ? "ExecuteWithoutResultsAsync" : "ExecuteAsync")));
        await Assert.That(result).IsEqualTo(argumentFailure ? "True:1:0:0" : "True:1:0:1");
    }

    [Test]
    public async Task RecoverableFailuresDoNotCancelConcurrentSteps()
    {
        var result = await Run(ConcurrentSteps + """
            public sealed class State { public int Attempts; }
            [StepPolicy(RetryCount = 1)]
            internal readonly ref struct Retry(State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) =>
                    ++state.Attempts == 1 ? Task.FromException<int>(new Exception("retry")) : Task.FromResult(42);
            }
            public sealed partial class Example : Pipeline
            {
                private void Configure(Builder p, State state) { var retry = p.Retry(state); var other = p.Start(); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().BuildServiceProvider();
                var state = new State();
                var result = await new Example(services, state).ExecuteAsync(otherWork: async token =>
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    return 7;
                });
                return $"{result.Retry}:{result.Other}:{state.Attempts}";
                """));
        await Assert.That(result).IsEqualTo("42:7:2");
    }
}
