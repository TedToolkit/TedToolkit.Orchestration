using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ParallelEntrypointsDirectlyCallStepsWithoutBranchesOrNestedMethods()
    {
        var generated = await Generate(NamedSteps + ConcurrentSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, Func<CancellationToken, Task<int>> rootWork,
                Func<int, CancellationToken, Task<int>> leftWork,
                Func<int, CancellationToken, Task<int>> rightWork)
                {
                    var root = p.Start(rootWork);
                    var left = p.After(root, leftWork);
                    var right = p.After(root, rightWork);
                    p.Add(left, right);
                }
            }
            """);
        await NoErrors(generated);
        var owner = generated.Compilation.GetTypeByMetadataName("Example")!;
        var root = (MethodDeclarationSyntax)owner.GetMembers("RunRoot0Async").OfType<IMethodSymbol>().Single()
            .DeclaringSyntaxReferences.Single().GetSyntax();
        await Assert.That(root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Count(call => call.Expression.ToString() == "executionToken.ThrowIfCancellationRequested")).IsEqualTo(3);
        foreach (var name in new[] { "ExecuteAsync", "ExecuteWithoutResultsAsync" })
        {
            var syntax = (MethodDeclarationSyntax)owner.GetMembers(name + "Core").OfType<IMethodSymbol>().Single()
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
            internal static class FailStepMethods
            {
                [Step]
                internal static int Fail(State state, CancellationToken token)
                {
                    state.Constructed++;
                    throw state.Expected;
                }
            }
            internal static class TouchStepMethods
            {
                [Step]
                internal static void Touch(int value, State state, CancellationToken token) => state.Downstream++;
            }
            public static partial class Example
            {
                private static State Read(State state) => state.ArgumentFailure ? throw state.Expected : state;
                [Pipeline]
                public static void Configuration(StepGraph p, State state, Func<CancellationToken, Task<int>> slowWork)
                {
                    var slow = p.Start(slowWork);
                    var failed = p.Fail(Read(state));
                    p.Touch(failed, state);
                }
            }
            """ + AsyncScenario("""
                var state = new State { ArgumentFailure = ARGUMENT };
                var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var cleaned = 0;
                Task execution = new Example.ConfigurationPipeline().METHOD(state, slowWork: async token =>
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
            internal static class RetryStepMethods
            {
                [Step]
                internal static Task<int> Retry(State state, CancellationToken token) =>
                    ++state.Attempts == 1 ? Task.FromException<int>(new Exception("retry")) : Task.FromResult(42);
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, State state,
                Func<CancellationToken, Task<int>> otherWork)
                {
                    var retry = p.Retry(state).WithRetry(1);
                    var other = p.Start(otherWork);
                }
            }
            """ + AsyncScenario("""
                var state = new State();
                var result = await new Example.ConfigurationPipeline().ExecuteAsync(state, otherWork: async token =>
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
