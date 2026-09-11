using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    private const string NamedSteps = """
        internal static class AddStepMethods
        {
            [Step]
            internal static int Add(int a, int b, CancellationToken token) => a + b;
        }
        public sealed class Sink { public int Value; public int Calls; }
        internal static class StoreStepMethods
        {
            [Step]
            internal static void Store(int value, [FromServices] Sink sink, CancellationToken token) { sink.Value = value; sink.Calls++; }
        }
        """;

    [Test]
    public async Task InputsFixedValuesAliasesAndTypedResultsWorkAcrossInvocations()
    {
        var result = await Run(NamedSteps + """
            internal static class LoadStepMethods
            {
                [Step]
                internal static Task<int> Load(int value, CancellationToken token) => Work(value);
                private static async Task<int> Work(int value) { await Task.Yield(); return value; }
            }
            internal static class NotifyStepMethods
            {
                [Step]
                internal static Task Notify([FromServices] Sink sink, CancellationToken token) { sink.Calls++; return Task.CompletedTask; }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, int fixedValue, int loadValue)
                {
                    var load = p.Load(loadValue);
                    var alias = load;
                    var sum = p.Add(alias, fixedValue);
                    p.Store(sum);
                    p.Notify();
                }
            }
            """ + AsyncScenario("""
                var sink = new Sink();
                using var services = new ServiceCollection().AddSingleton(sink).BuildServiceProvider();
                var executor = new Example.ConfigurationPipeline(services);
                var first = await executor.ExecuteAsync(fixedValue: 2, loadValue: 40);
                var second = await executor.ExecuteAsync(fixedValue: 2, loadValue: 8);
                await executor.ExecuteAsync(fixedValue: 2, loadValue: 3);
                return $"{first.Load}:{first.Sum}:{second.Sum}:{sink.Value}:{sink.Calls}:{typeof(Example.ConfigurationResult).IsValueType}:{typeof(StepGraph).IsValueType}";
                """));
        await Assert.That(result).IsEqualTo("40:42:10:5:6:True:True");
    }

    [Test]
    public async Task UnnamedNodesUseTypeAndZeroBasedRegistrationIndex()
    {
        var result = await Run(NamedSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, int add0A, int add0B, int add1A) { p.Add(add0A, add0B); p.Add(add1A, b: 5); }
            }
            """ + AsyncScenario("""
                var result = new Example.ConfigurationPipeline().Execute(add0A: 1, add0B: 2, add1A: 4);
                return $"{result.Add0}:{result.Add1}";
                """));
        await Assert.That(result).IsEqualTo("3:9");
    }

    [Test]
    [Arguments("e.Execute();", "CS7036")]
    [Arguments("e.Execute(sumA: \"bad\", sumB: 1);", "CS1503")]
    [Arguments("e.Execute(typo: 1, sumB: 2);", "CS1739")]
    public async Task CompilerChecksGeneratedExecutionArguments(string call, string diagnostic)
    {
        var generated = await Generate(NamedSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, int sumA, int sumB) { var sum = p.Add(sumA, sumB); } }
            """ + AsyncScenario("var e = new Example.ConfigurationPipeline(); " + call + " return string.Empty;"));
        await Assert.That(generated.Diagnostics.Any(item => item.Id == diagnostic)).IsTrue();
    }

    [Test]
    [Arguments("var a = p.Add(); var alias = a; alias = p.Add();")]
    [Arguments("if (DateTime.Now.Ticks > 0) p.Add();")]
    [Arguments("var alias = p; alias.Add();")]
    [Arguments("p.Add(p.Add(), 1);")]
    [Arguments("if (DateTime.Now.Ticks > 0) return; p.Add();")]
    [Arguments("for (var i = 0; i < 2; i++) p.Add();")]
    public async Task DynamicOrReassignedGraphsAreRejected(string body)
    {
        var generated = await Generate(NamedSteps + "public static partial class Example { public static void Configuration(StepGraph p) { " + body + " } }");
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

    [Test]
    public async Task DependencyNullabilityIsStillAnError()
    {
        var generated = await Generate("""
            internal static class SourceSteps { [Step] internal static string? Source(CancellationToken token) => null; }
            internal static class ConsumeSteps { [Step] internal static void Consume(string value, CancellationToken token) {} }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p) { var source = p.Source(); p.Consume(source); } }
            """);
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP001")).IsTrue();
    }

    [Test]
    public async Task FixedDefaultAndNullDoNotBecomeExecutionParameters()
    {
        var result = await Run(NamedSteps + """
            internal static class TextSteps { [Step] internal static string? Text(string? value, CancellationToken token) => value; }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p) { var zero = p.Add(default(int), 2); var text = p.Text((string?)null); }
            }
            """ + AsyncScenario("""
                var results = new Example.ConfigurationPipeline().Execute();
                return $"{results.Zero}:{results.Text is null}";
                """));
        await Assert.That(result).IsEqualTo("2:True");
    }

    [Test]
    public async Task EmptyConfigurationProducesParameterlessExecutions()
    {
        var result = await Run("""
            public static partial class Empty
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline) {} }
            """ + AsyncScenario("""
                var e = new Empty.ConfigurationPipeline();
                e.Execute();
                return "done";
                """));
        await Assert.That(result).IsEqualTo("done");
    }

    [Test]
    public async Task SynchronousCompletionOnlyExecutionStillAllocatesZeroBytes()
    {
        var result = await Run(NamedSteps + """
            internal static class CaptureSteps { [Step] internal static void Capture(int value, Sink sink, CancellationToken token) => sink.Value = value; }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, int value, Sink sink) { var a = p.Add(value, 1); var b = p.Add(a, 2); p.Capture(b, sink); }
            }
            """ + AsyncScenario("""
                var sink = new Sink(); var executor = new Example.ConfigurationPipeline();
                for (var i = 0; i < 1000; i++) executor.Execute(i, sink);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++) executor.Execute(i, sink);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                await Task.CompletedTask;
                return $"{allocated}:{sink.Value}";
                """));
        await Assert.That(result).IsEqualTo("0:1002");
    }

    [Test]
    [Arguments(false, 0)] [Arguments(false, 1)] [Arguments(false, 2)]
    [Arguments(true, 0)] [Arguments(true, 1)] [Arguments(true, 2)]
    public async Task SynchronousFailureAndCancellationThrowDirectly(bool discard, int mode)
    {
        var body = """
            using var services = new ServiceCollection().BuildServiceProvider();
            using var cancellation = new CancellationTokenSource();
            Exception expected = MODE == 1 ? new OperationCanceledException() : new InvalidOperationException("original");
            var executor = new Example.ConfigurationPipeline();
            if (MODE == 2) cancellation.Cancel();
            
            
            try { executor.METHOD(expected, cancellation.Token); return "unexpected"; }
            catch (Exception actual)
            {
                if (MODE == 2) return $"{actual is OperationCanceledException}:{actual is OperationCanceledException canceled && canceled.CancellationToken == cancellation.Token}";
                return $"{(actual is OperationCanceledException) == (MODE == 1)}:{ReferenceEquals(expected, actual)}";
            }
            """.Replace("MODE", mode.ToString()).Replace("METHOD", "Execute");
        var result = await Run("""
            internal static class FailSteps { [Step] internal static void Fail(Exception error, CancellationToken token) => throw error; }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, Exception error) { p.Fail(error); } }
            """ + AsyncScenario(body));
        await Assert.That(result).IsEqualTo("True:True");
    }

    [Test]
    public async Task RetryReinvokesAndCleansUpSyncFunctionButCapturesFixedInputOnce()
    {
        var result = await Run("""
            public sealed class State { public int Attempts; public int Constructed; public int Disposed; public int Evaluations; }
            internal static class RetryStepMethods
            {
                [Step]
                internal static int Retry(State state, CancellationToken token)
                {
                    state.Constructed++;
                    try
                    {
                        if (++state.Attempts < 3) throw new InvalidOperationException();
                        return 42;
                    }
                    finally { state.Disposed++; }
                }
            }
            public static partial class Example
            {
                private static State Evaluate(State state) { state.Evaluations++; return state; }
                [Pipeline]
                public static void Configuration(StepGraph p, State state) { var result = p.Retry(Evaluate(state)).WithRetry(2); }
            }
            """ + AsyncScenario("""
                var state = new State(); var e = new Example.ConfigurationPipeline();
                var result = e.Execute(state);
                return $"{result.Result}:{state.Attempts}:{state.Constructed}:{state.Disposed}:{state.Evaluations}";
                """));
        await Assert.That(result).IsEqualTo("42:3:3:3:1");
    }

    [Test]
    public async Task PolicyBodiesAreOutsideMainFlowAndResultsRemainTyped()
    {
        var generated = await Generate(NamedSteps + """
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph p, int aA, int aB) { var a = p.Add(aA, aB); p.Store(a); } }
            """);
        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource.Contains("StepArgument<" )).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("List<") || generated.GeneratedSource.Contains("object[]") || generated.GeneratedSource.Contains("GetValue(inputs)") || generated.GeneratedSource.Contains("InterceptsLocation")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains("global::AddStepMethods.Add(")).IsTrue();
    }
    [Test]
    public async Task ConfigurationParametersAndStepNamesCannotShadowGeneratedLocals()
    {
        var result = await Run("""
            internal static class CaptureStepMethods
            {
                [Step]
                internal static int Capture(int index, int _owner, int __nodeIndex, CancellationToken token) => index + _owner + __nodeIndex;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline, int services)
                {
                    var node = pipeline.Capture(services, 2, 3);
                }
            }
            """ + AsyncScenario("""
                return (new Example.ConfigurationPipeline().Execute(1)).Node.ToString();
                """));
        await Assert.That(result).IsEqualTo("6");
    }

    [Test]
    public async Task SynchronousTimeoutStillObservesCooperativeCancellation()
    {
        var result = await Run("""
            internal static class WaitStepMethods
            {
                [Step]
                internal static int Wait(CancellationToken token) { token.WaitHandle.WaitOne(TimeSpan.FromSeconds(3)); return 42; }
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph pipeline) { pipeline.Wait().WithTimeout(30); } }
            """ + AsyncScenario("""
                try { new Example.ConfigurationPipeline().Execute(); return "unexpected"; }
                catch (TimeoutException) { return "timeout"; }
                """));
        await Assert.That(result).IsEqualTo("timeout");
    }

    [Test]
    [Arguments("public partial class Example { public static void Configuration(StepGraph p) {} }")]
    [Arguments("public static partial class Example<T> { public static void Configuration(StepGraph p) {} }")]
    [Arguments("public static class Example { public static void Configuration(StepGraph p) {} }")]
    [Arguments("public static partial class Example { public static void Configuration(StepGraph p) { var sum = p.Add(1, 2); var Sum = p.Add(3, 4); } }")]
    public async Task InvalidExecutorDeclarationsAndResultNamesAreRejected(string declaration)
    {
        var generated = await Generate(NamedSteps + declaration);
        await Assert.That(generated.Diagnostics.Any(item => item.Id == "TTP009")).IsTrue();
    }

}




