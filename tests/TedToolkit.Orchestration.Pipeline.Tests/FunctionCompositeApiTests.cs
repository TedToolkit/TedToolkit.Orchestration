namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task ArbitraryCompositeFunctionNameGeneratesMatchingPipelineClass()
    {
        var value = await Run("""
            internal static class MathSteps
            {
                [Step]
                internal static int Add(int left, int right, CancellationToken token = default) => left + right;
            }

            public static partial class Flow
            {
                [Pipeline]
                public static void Import(StepGraph steps, int value)
                {
                    var total = steps.Add(value, 2);
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Flow.ImportPipeline().Execute(40);
                    return Task.FromResult(result.Total.ToString());
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42");
    }

    [Test]
    public async Task FunctionCompositeProjectsInputsServicesAndPipelineFacade()
    {
        var value = await Run("""
            public sealed class Offset { public int Value => 1; }

            internal static class MathSteps
            {
                [Step]
                internal static int Add(int left, int right, CancellationToken token) => left + right;
            }

            public static partial class Sum
            {
                [Pipeline]
                public static void Configuration(
                    StepGraph steps,
                    int left,
                    int right = 1,
                    [FromServices] Offset offset = null!,
                    CancellationToken token = default)
                {
                    var total = steps.Add(left, right + offset.Value);
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var services = new ServiceCollection().AddSingleton<Offset>().BuildServiceProvider();
                    var result = new Sum.ConfigurationPipeline(services).Execute(40);
                    return Task.FromResult(result.Total.ToString());
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42");
    }

    [Test]
    public async Task PipelineCanSelectLeafWithoutGeneratingCompositeObjectContracts()
    {
        var generated = await Generate("""
            public static partial class MathSteps
            {
                [Step, Pipeline(Name = "Sum")]
                public static int Add(int left, int right = 2, CancellationToken token = default) => left + right;
            }
            """);

        await NoErrors(generated);
        await Assert.That(generated.GeneratedSource).Contains("sealed class SumPipeline");
        await Assert.That(generated.GeneratedSource).DoesNotContain("ICompositeStep");
        await Assert.That(generated.GeneratedSource).DoesNotContain("DisplayName");
    }

    [Test]
    public async Task OmittedStepEnumDefaultIsPortableAcrossGeneratedNamespaces()
    {
        var value = await Run("""
            namespace Producer
            {
                public enum Mode { First = 7 }

                internal static class ReadSteps
                {
                    [Step]
                    internal static int Read(
                        Mode mode = Mode.First,
                        CancellationToken token = default) => (int)mode;
                }
            }

            namespace Consumer
            {
                public static partial class Root
                {
                    [Pipeline]
                    public static void Configuration(StepGraph steps)
                    {
                        var value = steps.Read();
                    }
                }
            }

            public static class Scenario
            {
                public static Task<string> Run() => Task.FromResult(
                    new Consumer.Root.ConfigurationPipeline().Execute().Value.ToString());
            }
            """);

        await Assert.That(value).IsEqualTo("7");
    }

    [Test]
    public async Task RootLeafResolvesOrdinaryAndKeyedServicesBeforeCreatingLogger()
    {
        var value = await Run("""
            public sealed class Marker;
            public sealed class KeyedMarker;

            public static class Observations
            {
                public static readonly global::System.Collections.Generic.List<string> Events = new();
                public static int Calls;
                public static int LoggerCreations;

                public static Marker FailMarker()
                {
                    Events.Add("ordinary-fail");
                    throw new InvalidOperationException("ordinary failed");
                }
            }

            public sealed class CaptureLoggerFactory : ILoggerFactory
            {
                public ILogger CreateLogger(string categoryName)
                {
                    Observations.Events.Add("logger");
                    Observations.LoggerCreations++;
                    return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                }
                public void AddProvider(ILoggerProvider provider) { }
                public void Dispose() { }
            }

            public static partial class RootSteps
            {
                [Step, Pipeline]
                public static string Run(
                    [FromServices] ILogger logger,
                    [FromServices] Marker marker,
                    [FromServices("key")] KeyedMarker keyed,
                    CancellationToken token = default)
                {
                    Observations.Events.Add("call");
                    Observations.Calls++;
                    return string.Join(",", Observations.Events);
                }
            }

            public static class Scenario
            {
                private static ServiceProvider Services(bool failing)
                {
                    var services = new ServiceCollection();
                    if (failing)
                        services.AddSingleton<Marker>(_ => Observations.FailMarker());
                    else
                        services.AddSingleton<Marker>(_ =>
                        {
                            Observations.Events.Add("ordinary");
                            return new Marker();
                        });
                    services.AddKeyedSingleton<KeyedMarker>("key", (_, _) =>
                    {
                        Observations.Events.Add("keyed");
                        return new KeyedMarker();
                    });
                    services.AddSingleton<ILoggerFactory>(_ =>
                    {
                        Observations.Events.Add("logger-factory");
                        return new CaptureLoggerFactory();
                    });
                    return services.BuildServiceProvider();
                }

                public static Task<string> Run()
                {
                    using var working = Services(false);
                    var success = new RootSteps.RunPipeline(working).Execute();

                    Observations.Events.Clear();
                    using var failing = Services(true);
                    try
                    {
                        new RootSteps.RunPipeline(failing).Execute();
                    }
                    catch (InvalidOperationException exception) when (exception.Message == "ordinary failed")
                    {
                    }

                    return Task.FromResult($"{success}|{string.Join(",", Observations.Events)}|" +
                        $"{Observations.LoggerCreations}:{Observations.Calls}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo(
            "ordinary,keyed,logger-factory,logger,call|ordinary-fail|1:1");
    }

    [Test]
    public async Task RootLeafBindsEveryNonGenericLoggerParameterToOneCategoryLogger()
    {
        var value = await Run("""
            public sealed class CaptureLoggerFactory : ILoggerFactory
            {
                public static int Creations;
                public ILogger CreateLogger(string categoryName)
                {
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

            public static partial class LoggedSteps
            {
                [Step, Pipeline]
                public static bool Same(
                    [FromServices] ILogger first,
                    [FromServices] ILogger second,
                    CancellationToken token = default) => ReferenceEquals(first, second);
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    using var services = new ServiceCollection()
                        .AddSingleton<ILoggerFactory, CaptureLoggerFactory>()
                        .BuildServiceProvider();
                    var same = new LoggedSteps.SamePipeline(services).Execute();
                    return Task.FromResult($"{same}:{CaptureLoggerFactory.Creations}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("True:1");
    }

    [Test]
    public async Task CompositeBindsEveryNonGenericLoggerParameterToOneCategoryLogger()
    {
        var value = await Run("""
            public sealed class CaptureLoggerFactory : ILoggerFactory
            {
                public static int Creations;
                public ILogger CreateLogger(string categoryName)
                {
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

            internal static class ValueSteps
            {
                [Step]
                internal static bool Value(bool value, CancellationToken token = default) => value;
            }

            public static partial class LoggedComposite
            {
                [Pipeline]
                public static void Configuration(
                    StepGraph steps,
                    [FromServices] ILogger first,
                    [FromServices] ILogger second)
                {
                    var same = steps.Value(ReferenceEquals(first, second));
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    using var services = new ServiceCollection()
                        .AddSingleton<ILoggerFactory, CaptureLoggerFactory>()
                        .BuildServiceProvider();
                    var same = new LoggedComposite.ConfigurationPipeline(services).Execute().Same;
                    return Task.FromResult($"{same}:{CaptureLoggerFactory.Creations}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("True:1");
    }

    [Test]
    public async Task RootLeafAndCompositeResolveDynamicServicesAsObject()
    {
        var value = await Run("""
            public sealed class DynamicService { public int Value => 42; }

            public static partial class DynamicSteps
            {
                [Step, Pipeline]
                public static int Read(
                    [FromServices] dynamic service,
                    CancellationToken token = default) => service.Value;

                [Step]
                public static int Value(int value, CancellationToken token = default) => value;
            }

            public static partial class DynamicComposite
            {
                [Pipeline]
                public static void Configuration(
                    StepGraph steps,
                    [FromServices] dynamic service)
                {
                    var value = steps.Value((int)service.Value);
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    using var services = new ServiceCollection()
                        .AddSingleton<object>(new DynamicService())
                        .BuildServiceProvider();
                    var leaf = new DynamicSteps.ReadPipeline(services).Execute();
                    var composite = new DynamicComposite.ConfigurationPipeline(services).Execute().Value;
                    return Task.FromResult($"{leaf}:{composite}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42:42");
    }

    [Test]
    public async Task NestedStepLoggerUsesEveryCompositeDisplaySegment()
    {
        var value = await Run("""
            public sealed class CaptureLoggerFactory : ILoggerFactory
            {
                public static string Category = "";
                public ILogger CreateLogger(string categoryName)
                {
                    Category = categoryName;
                    return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                }
                public void AddProvider(ILoggerProvider provider) { }
                public void Dispose() { }
            }

            internal static class LeafSteps
            {
                [Step]
                internal static string Save(
                    [FromServices] ILogger logger,
                    CancellationToken token) => CaptureLoggerFactory.Category;
            }

            public static partial class Inner
            {
                public static void Build(StepGraph steps)
                {
                    var save = steps.Save().WithDisplayName("Save");
                }
            }

            public static partial class Root
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var import = steps.Build().WithDisplayName("Import");
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var services = new ServiceCollection()
                        .AddSingleton<ILoggerFactory, CaptureLoggerFactory>()
                        .BuildServiceProvider();
                    var result = new Root.ConfigurationPipeline(services).Execute();
                    return Task.FromResult(result.Import.Save);
                }
            }
            """);

        await Assert.That(value).IsEqualTo("LeafSteps.Save[Root/Import/Save]");
    }
}
