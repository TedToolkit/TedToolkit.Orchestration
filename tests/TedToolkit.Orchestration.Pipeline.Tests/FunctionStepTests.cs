using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task FunctionStepsUseTheExistingCompositeApiAndExecuteDirectly()
    {
        var value = await Run("""
            public interface IFormatter { string Format(int value); }
            public sealed class Formatter : IFormatter
            {
                public string Format(int value) => $"Value:{value}";
            }
            internal static class Leaves
            {
                [Step]
                internal static int Add(int left, int right, CancellationToken token) => left + right;

                [Step]
                internal static Task<string> Format(
                    int value,
                    [FromServices] IFormatter formatter,
                    CancellationToken token) => Task.FromResult(formatter.Format(value));
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value)
                {
                    var sum = steps.Add(value, 2);
                    var formatted = steps.Format(sum);
                }
            }
            """ + AsyncScenario("""
                var services = new ServiceCollection().AddSingleton<IFormatter, Formatter>().BuildServiceProvider();
                var result = await new Example.ConfigurationPipeline(services).ExecuteAsync(40);
                return result.Formatted;
                """));

        await Assert.That(value).IsEqualTo("Value:42");
    }

    [Test]
    public async Task NonGenericLoggerUsesMethodIdentityAndDisplayNameOnceBeforeRetries()
    {
        var value = await Run("""
            public sealed class CaptureLoggerFactory : ILoggerFactory
            {
                public static string Category = "";
                public static int Creations;
                public ILogger CreateLogger(string categoryName)
                {
                    Category = categoryName;
                    Creations++;
                    return new CaptureLogger();
                }
                public void AddProvider(ILoggerProvider provider) { }
                public void Dispose() { }
                private sealed class CaptureLogger : ILogger
                {
                    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                    public bool IsEnabled(LogLevel level) => true;
                    public void Log<TState>(LogLevel level, EventId eventId, TState state,
                        Exception? exception, Func<TState, Exception?, string> formatter) { }
                }
            }
            internal static class LoggedSteps
            {
                public static int Attempts;

                [Step]
                internal static string Eventually(
                    [FromServices] ILogger logger,
                    CancellationToken token)
                {
                    if (Attempts++ == 0) throw new InvalidOperationException("retry");
                    return CaptureLoggerFactory.Category;
                }
            }
            public static partial class Logging
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var result = steps.Eventually().WithDisplayName("Friendly").WithRetry(1);
                }
            }
            """ + AsyncScenario("""
                var services = new ServiceCollection()
                    .AddSingleton<ILoggerFactory, CaptureLoggerFactory>()
                    .BuildServiceProvider();
                var result = new Logging.ConfigurationPipeline(services).Execute();
                return $"{result.Result}:{CaptureLoggerFactory.Creations}:{LoggedSteps.Attempts}";
                """));

        await Assert.That(value).IsEqualTo("LoggedSteps.Eventually[Logging/Friendly]:1:2");
    }

    [Test]
    public async Task GenericLoggerIsResolvedAsAnOrdinaryService()
    {
        var value = await Run("""
            public sealed class LogCategory;
            public sealed class CaptureLogger : ILogger<LogCategory>
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel level) => true;
                public void Log<TState>(LogLevel level, EventId eventId, TState state,
                    Exception? exception, Func<TState, Exception?, string> formatter) { }
            }
            internal static class LoggedSteps
            {
                [Step]
                internal static string Read(
                    [FromServices] ILogger<LogCategory> logger,
                    CancellationToken token) => logger.GetType().Name;
            }
            public static partial class Logging
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var result = steps.Read();
                }
            }
            """ + AsyncScenario("""
                var services = new ServiceCollection()
                    .AddSingleton<ILogger<LogCategory>, CaptureLogger>()
                    .BuildServiceProvider();
                return new Logging.ConfigurationPipeline(services).Execute().Result;
                """));

        await Assert.That(value).IsEqualTo("CaptureLogger");
    }

    [Test]
    public async Task UnsupportedFunctionStepShapesAreRejected()
    {
        var generated = await Generate("""
            internal static class InvalidSteps
            {
                [Step]
                private static int Hidden(CancellationToken token) => 1;

                [Step]
                internal static int MissingToken() => 1;

                [Step]
                internal static int Overloaded(int value, CancellationToken token) => value;

                [Step]
                internal static int Overloaded(string value, CancellationToken token) => value.Length;
            }
            """);

        await Assert.That(generated.Diagnostics.Count(
            diagnostic => diagnostic.Id == "TTP013")).IsEqualTo(4);
    }

    [Test]
    public async Task RuntimeRemovesObjectStepContracts()
    {
        var runtime = typeof(StepGraph).Assembly;
        foreach (var name in new[]
        {
            "IStep",
            "IStep`1",
            "IAsyncStep",
            "IAsyncStep`1",
            "ICompositeStep`1",
            "IAsyncCompositeStep`1",
        })
            await Assert.That(runtime.GetType(
                "TedToolkit.Orchestration.Pipeline." + name)).IsNull();

        await Assert.That(runtime.GetType(
            "TedToolkit.Orchestration.Pipeline.Attributes.CompositeStepAttribute")).IsNull();
        await Assert.That(runtime.GetType(
            "TedToolkit.Orchestration.Pipeline.Attributes.StepLoggerAttribute")).IsNull();
    }

    [Test]
    public async Task FunctionLeafDoesNotReceiveGeneratedInstanceContext()
    {
        var generated = await Generate("""
            internal static class Leaves
            {
                [Step]
                internal static int Value(CancellationToken token) => 42;
            }
            public static partial class Example
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var value = steps.Value();
                }
            }
            """);

        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource.Contains(
            "partial class Leaves\n")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains(
            "global::Leaves.Value(")).IsTrue();
    }
}
