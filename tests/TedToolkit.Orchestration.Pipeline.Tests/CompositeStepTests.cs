using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task CompositeStepRunsAsRootThroughGeneratedPipeline()
    {
        var value = await Run("""
            internal static class AddStepMethods
            {
                [Step]
                internal static Task<int> Add(int left, int right, CancellationToken token) => Task.FromResult(left + right);
            }

            public static partial class Sum
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int left, int right)
                {
                    var total = steps.Add(left, right);
                }
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var pipeline = new Sum.ConfigurationPipeline();
                    var result = await pipeline.ExecuteAsync(40, 2);
                    return $"{result.Total}";
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42");
    }

    [Test]
    public async Task ServiceRequirementControlsGeneratedCompositeSignatures()
    {
        var serviceFree = await Generate("""
            internal static class ValueStepMethods
            {
                [Step]
                internal static int Value(CancellationToken token) => 42;
            }
            public static partial class ServiceFree
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var value = steps.Value();
                }
            }
            """);
        await NoErrors(serviceFree);

        var serviceBound = await Generate("""
            public sealed class ValueProvider { public int Value => 42; }
            internal static class ReadStepMethods
            {
                [Step]
                internal static int Read([FromServices] ValueProvider provider, CancellationToken token) => provider.Value;
            }
            public static partial class ServiceBound
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var value = steps.Read();
                }
            }
            """);
        await NoErrors(serviceBound);

        await Assert.That(serviceFree.GeneratedSource.Contains("IServiceProvider")).IsFalse();
        await Assert.That(serviceFree.GeneratedSource.Contains("EmptyServices")).IsFalse();
        await Assert.That(serviceFree.GeneratedSource.Contains("Pipeline.Generated.ExecutionSupport")).IsFalse();
        await Assert.That(serviceBound.GeneratedSource.Contains(
            "global::System.IServiceProvider __services")).IsTrue();
    }

    [Test]
    public async Task PublicCompositeCanBeNestedFromAnotherCompilation()
    {
        var library = await Generate("""
            internal static class AddOneStepMethods
            {
                [Step]
                internal static int AddOne(int value, CancellationToken token) => value + 1;
            }
            public static partial class Increment
            {
                [Pipeline]
                public static void Build(StepGraph steps, int value)
                {
                    var incremented = steps.AddOne(value);
                }
            }
            """, assemblyName: "CompositeLibrary");
        await NoErrors(library);
        using var image = new MemoryStream();
        var emitted = library.Compilation.Emit(image);
        await Assert.That(emitted.Success).IsTrue();

        var consumer = await Generate("""
            public static partial class Consumer
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value)
                {
                    var increment = steps.Build(value);
                }
            }
            """, MetadataReference.CreateFromImage(image.ToArray()), "CompositeConsumer");
        await NoErrors(consumer);
        await Assert.That(consumer.GeneratedSource).Contains(
            "global::Increment.Build");
    }

    [Test]
    public async Task SameNamedCompositesBindByExactGeneratedFactorySymbol()
    {
        var value = await Run("""
            namespace Alpha
            {
                internal static class AddOneStepMethods
                {
                    [Step]
                    internal static int AddOne(int value, CancellationToken token) => value + 1;
                }
                public static partial class Transform
                {
                    [Pipeline]
                    public static void Build(StepGraph steps, int value)
                    {
                        var number = steps.AddOne(value);
                    }
                }
            }
            namespace Beta
            {
                internal static class AppendStepMethods
                {
                    [Step]
                    internal static string Append(string value, CancellationToken token) => value + "!";
                }
                public static partial class Transform
                {
                    [Pipeline]
                    public static void Build(StepGraph steps, string value)
                    {
                        var text = steps.Append(value);
                    }
                }
            }
            public static partial class Both
            {
                [Pipeline]
                public static void Run(StepGraph steps, int value, string text)
                {
                    var number = Alpha_Transform_BuildExtensions.Build(steps, value);
                    var textValue = Beta_Transform_BuildExtensions.Build(steps, text);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Both.RunPipeline().Execute(1, "x");
                    return Task.FromResult($"{result.Number.Number}:{result.TextValue.Text}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("2:x!");
    }

    [Test]
    public async Task GeneratedFactoryNamesPreserveNamespaceSegmentIdentity()
    {
        var value = await Run("""
            namespace A_B
            {
                internal static class IncrementStepMethods
                {
                    [Step]
                    internal static int Increment(int value, CancellationToken token) => value + 1;
                }
            }
            namespace A.B
            {
                internal static class IncrementStepMethods
                {
                    [Step]
                    internal static int Increment(int value, CancellationToken token) => value + 2;
                }
            }
            public static partial class Both
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value)
                {
                    var first = A__B_IncrementStepMethods_IncrementExtensions.Increment(steps, value);
                    var second = A_B_IncrementStepMethods_IncrementExtensions.Increment(steps, value);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Both.ConfigurationPipeline().Execute(1);
                    return Task.FromResult($"{result.First}:{result.Second}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("2:3");
    }

    [Test]
    public async Task CompositeDependencyCyclesAreRejected()
    {
        var generated = await Generate("""
            public static partial class First
            {
                [Pipeline]
                public static void BuildFirst(StepGraph steps) { steps.BuildSecond(); }
            }
            public static partial class Second
            {
                [Pipeline]
                public static void BuildSecond(StepGraph steps) { steps.BuildFirst(); }
            }
            """);

        await Assert.That(generated.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "TTP009" &&
            diagnostic.GetMessage().Contains("dependency cycle", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task SameNamedPublicCompositesFromDifferentAssembliesCanBeSelectedExactly()
    {
        var alpha = await Generate("""
            namespace Alpha
            {
                internal static class AddOneStepMethods
                {
                    [Step]
                    internal static int AddOne(int value, CancellationToken token) => value + 1;
                }
                public static partial class Increment
                {
                    [Pipeline]
                    public static void Build(StepGraph steps, int value)
                    {
                        var output = steps.AddOne(value);
                    }
                }
            }
            """, assemblyName: "AlphaCompositeLibrary");
        var beta = await Generate("""
            namespace Beta
            {
                internal static class DoubleStepMethods
                {
                    [Step]
                    internal static int Double(int value, CancellationToken token) => value * 2;
                }
                public static partial class Increment
                {
                    [Pipeline]
                    public static void Build(StepGraph steps, int value)
                    {
                        var output = steps.Double(value);
                    }
                }
            }
            """, assemblyName: "BetaCompositeLibrary");
        await NoErrors(alpha);
        await NoErrors(beta);

        static byte[] Emit(GeneratedCompilation generated)
        {
            using var stream = new MemoryStream();
            var result = generated.Compilation.Emit(stream);
            if (!result.Success)
                throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
            return stream.ToArray();
        }

        var alphaImage = Emit(alpha);
        var betaImage = Emit(beta);
        System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(alphaImage));
        System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(betaImage));
        var consumer = await GenerateWithReferences("""
            public static partial class Both
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value)
                {
                    var first = Alpha_Increment_BuildExtensions.Build(steps, value);
                    var second = Beta_Increment_BuildExtensions.Build(steps, value);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Both.ConfigurationPipeline().Execute(21);
                    return Task.FromResult($"{result.First.Output}:{result.Second.Output}");
                }
            }
            """, "CompositeConsumer", MetadataReference.CreateFromImage(alphaImage),
            MetadataReference.CreateFromImage(betaImage));
        await NoErrors(consumer);
        await Assert.That(consumer.GeneratedSource).Contains(
            "global::Alpha.Increment.Build");
        await Assert.That(consumer.GeneratedSource).Contains(
            "global::Beta.Increment.Build");

        using var consumerImage = new MemoryStream();
        var emitted = consumer.Compilation.Emit(consumerImage);
        await Assert.That(string.Join("\n", emitted.Diagnostics.Where(
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))).IsEqualTo("");
        consumerImage.Position = 0;
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(consumerImage);
        var run = assembly.GetType("Scenario")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>();
        await Assert.That(await run()).IsEqualTo("22:42");
    }

    [Test]
    public async Task NestedCompositeUsesCallerOwnedServicesWithoutCreatingNestedPipeline()
    {
        var value = await Run("""
            public interface IOffset { int Value { get; } }
            public sealed class Offset : IOffset { public int Value => 2; }

            internal static class AddOffsetStepMethods
            {
                [Step]
                internal static Task<int> AddOffset(int value,
                [FromServices] IOffset offset, CancellationToken token) =>
                    Task.FromResult(value + offset.Value);
            }

            public static partial class Inner
            {
                [Pipeline]
                public static void Build(StepGraph steps, int value)
                {
                    var adjusted = steps.AddOffset(value);
                }
            }

            public static partial class Outer
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value)
                {
                    var inner = steps.Build(value);
                }
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var services = new ServiceCollection()
                        .AddSingleton<IOffset, Offset>()
                        .BuildServiceProvider();
                    var pipeline = new Outer.ConfigurationPipeline(services);
                    var result = await pipeline.ExecuteAsync(40);
                    return $"{result.Inner.Adjusted}";
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42");
    }

    [Test]
    public async Task ParentRetryRerunsCompositeWhileChildRetryRemainsLocal()
    {
        var value = await Run("""
            internal static class EventuallyStepMethods
            {
                public static int Attempts;
                [Step]
                internal static int Eventually(CancellationToken token)
                {
                    if (++Attempts <= 2) throw new InvalidOperationException("retry");
                    return 42;
                }
            }
            public static partial class InnerRetry
            {
                [Pipeline]
                public static void Build(StepGraph steps)
                {
                    var value = steps.Eventually().WithRetry(1);
                }
            }
            public static partial class OuterRetry
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var inner = steps.Build().WithRetry(1);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new OuterRetry.ConfigurationPipeline().Execute();
                    return Task.FromResult($"{result.Inner.Value}:{EventuallyStepMethods.Attempts}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42:3");
    }

    [Test]
    public async Task FunctionCompositeGeneratesNoInstanceContextOrCs0282Suppression()
    {
        var generated = await Generate("""
            internal static class ReadNameStepMethods
            {
                [Step]
                internal static int ReadName(int value, CancellationToken token) => value;
            }

            public static partial class Names
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, int value, [FromServices] ILogger __logger)
                {
                    var readable = steps.ReadName(value).WithDisplayName("Readable");
                }
            }
            """);

        await NoErrors(generated);
        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "CS0282")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains("required string DisplayName")).IsFalse();
        await Assert.That(generated.GeneratedSource).DoesNotContain("__TedToolkitCompositeStepState");
        await Assert.That(generated.GeneratedSource).DoesNotContain("__TedToolkitPrepareCompositeStep");
    }

    [Test]
    public async Task GeneratedContextHintNamesAreCollisionResistant()
    {
        var value = await Run("""
            namespace A_B
            {
                internal static class CStepMethods
                {
                    [Step]
                    internal static string C(CancellationToken token) => "first";
                }
            }
            namespace A
            {
                internal static class B_CStepMethods
                {
                    [Step]
                    internal static string B_C(CancellationToken token) => "second";
                }
            }
            public static partial class Both
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var first = steps.C();
                    var second = steps.B_C();
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Both.ConfigurationPipeline().Execute();
                    return Task.FromResult($"{result.First}:{result.Second}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("first:second");
    }

    [Test]
    public async Task CompositeHintNamesAreReadableAndDisambiguateOnlyRealCollisions()
    {
        var generated = await Generate("""
            namespace A_B
            {
                internal static partial class C
                {
                    [Pipeline]
                    public static void Configuration(StepGraph steps) { } }
            }
            namespace A
            {
                internal static partial class B_C
                {
                    [Pipeline]
                    public static void Configuration(StepGraph steps) { } }
            }
            namespace Demo
            {
                internal static partial class e
                {
                    [Pipeline]
                    public static void Configuration(StepGraph steps) { } }
                internal static partial class e\u0301
                {
                    [Pipeline]
                    public static void Configuration(StepGraph steps) { } }
            }
            namespace @class
            {
                public static partial class @event
                {
                    [Pipeline]
                    public static void Configuration(StepGraph steps) { }
                }
            }
            """);
        await NoErrors(generated);

        var hintNames = generated.Compilation.SyntaxTrees
            .Select(tree => Path.GetFileName(tree.FilePath))
            .Where(path => path.EndsWith(".CompositeStep.g.cs", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(hintNames.Length).IsEqualTo(5);
        await Assert.That(hintNames.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(5);
        await Assert.That(hintNames).Contains("A_B.C.Configuration.CompositeStep.g.cs");
        await Assert.That(hintNames).Contains("A.B_C.Configuration.CompositeStep.g.cs");
        await Assert.That(hintNames).Contains("class.event.Configuration.CompositeStep.g.cs");
        await Assert.That(hintNames.Count(name => name.StartsWith("Demo.e", StringComparison.Ordinal))).IsEqualTo(2);
        await Assert.That(hintNames.Any(name => System.Text.RegularExpressions.Regex.IsMatch(
            name, @"\.[0-9a-f]{64}\.CompositeStep\.g\.cs$"))).IsFalse();
    }

    [Test]
    public async Task LoggerAndDisplayNameAreCreatedOnceBeforeLeafRetries()
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
                    public void Log<TState>(LogLevel level, EventId id, TState state,
                        Exception? exception, Func<TState, Exception?, string> formatter) { }
                }
            }

            internal static class WorkStepMethods
            {
                private static int attempts;
                [Step]
                internal static string Work(
                    [FromServices] ILogger logger,
                    CancellationToken token)
                {
                    logger.LogInformation("attempt");
                    if (++attempts == 1) throw new InvalidOperationException("retry");
                    return CaptureLoggerFactory.Category;
                }
            }

            public static partial class LoggedWork
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var work = steps.Work().WithDisplayName("Friendly").WithRetry(1);
                }
            }

            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var factory = new CaptureLoggerFactory();
                    var services = new ServiceCollection()
                        .AddSingleton<ILoggerFactory>(factory)
                        .BuildServiceProvider();
                    var result = new LoggedWork.ConfigurationPipeline(services).Execute();
                    return Task.FromResult($"{result.Work}:{CaptureLoggerFactory.Category}:{CaptureLoggerFactory.Creations}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo(
            "WorkStepMethods.Work[LoggedWork/Friendly]:WorkStepMethods.Work[LoggedWork/Friendly]:1");
    }

    [Test]
    public async Task RootCompositeCreatesItsConfiguredLoggerBeforeExecution()
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
                    return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                }
                public void AddProvider(ILoggerProvider provider) { }
                public void Dispose() { }
            }
            internal static class TouchStepMethods
            {
                public static int Attempts;
                [Step]
                internal static int Touch(CancellationToken token) { Attempts++; return 42; }
            }
            public static partial class LoggedRoot
            {
                [Pipeline]
                public static void Build(StepGraph steps, [FromServices] ILogger __logger)
                {
                    var value = steps.Touch();
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var services = new ServiceCollection()
                        .AddSingleton<ILoggerFactory, CaptureLoggerFactory>()
                        .BuildServiceProvider();
                    var result = new LoggedRoot.BuildPipeline(services).Execute();
                    return Task.FromResult($"{result.Value}:{CaptureLoggerFactory.Category}:{CaptureLoggerFactory.Creations}:{TouchStepMethods.Attempts}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42:LoggedRoot.Build[LoggedRoot]:1:1");
    }

    [Test]
    public async Task NestedCompositeCreatesOneConfiguredLoggerAcrossParentRetries()
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
                    return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                }
                public void AddProvider(ILoggerProvider provider) { }
                public void Dispose() { }
            }
            internal static class EventuallyStepMethods
            {
                public static int Attempts;
                [Step]
                internal static int Eventually(CancellationToken token)
                {
                    if (++Attempts == 1) throw new InvalidOperationException("retry");
                    return 42;
                }
            }
            public static partial class LoggedInner
            {
                [Pipeline]
                public static void Build(StepGraph steps, [FromServices] ILogger __logger)
                {
                    var value = steps.Eventually();
                }
            }
            public static partial class LoggedOuter
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var inner = steps.Build()
                        .WithDisplayName("Friendly Composite")
                        .WithRetry(1);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var services = new ServiceCollection()
                        .AddSingleton<ILoggerFactory, CaptureLoggerFactory>()
                        .BuildServiceProvider();
                    var result = new LoggedOuter.ConfigurationPipeline(services).Execute();
                    return Task.FromResult($"{result.Inner.Value}:{CaptureLoggerFactory.Category}:{CaptureLoggerFactory.Creations}:{EventuallyStepMethods.Attempts}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo(
            "42:LoggedInner.Build[LoggedOuter/Friendly Composite]:1:2");
    }

    [Test]
    public async Task MissingRootCompositeLoggerFailsBeforeTheFirstChildAttempt()
    {
        var value = await Run("""
            internal static class TouchStepMethods
            {
                public static int Attempts;
                [Step]
                internal static void Touch(CancellationToken token) => Attempts++;
            }
            public static partial class LoggedRoot
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, [FromServices] ILogger __logger)
                {
                    steps.Touch();
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var services = new ServiceCollection().BuildServiceProvider();
                    try
                    {
                        new LoggedRoot.ConfigurationPipeline(services).Execute();
                        return Task.FromResult("unexpected");
                    }
                    catch (InvalidOperationException)
                    {
                        return Task.FromResult(TouchStepMethods.Attempts.ToString());
                    }
                }
            }
            """);

        await Assert.That(value).IsEqualTo("0");
    }

    [Test]
    public async Task LoggerCreationFailureHappensBeforeAttemptsAndIsNotRetried()
    {
        var value = await Run("""
            public sealed class ThrowingLoggerFactory : ILoggerFactory
            {
                public static int Creations;
                public ILogger CreateLogger(string categoryName)
                {
                    Creations++;
                    throw new InvalidOperationException("logger");
                }
                public void AddProvider(ILoggerProvider provider) { }
                public void Dispose() { }
            }
            internal static class LoggedStepMethods
            {
                public static int Attempts;
                [Step]
                internal static void Logged(
                    [FromServices] ILogger logger,
                    CancellationToken token) => Attempts++;
            }
            public static partial class LoggingFailure
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    steps.Logged().WithRetry(2);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var services = new ServiceCollection()
                        .AddSingleton<ILoggerFactory, ThrowingLoggerFactory>()
                        .BuildServiceProvider();
                    try
                    {
                        new LoggingFailure.ConfigurationPipeline(services).Execute();
                        return Task.FromResult("unexpected");
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Task.FromResult($"{ThrowingLoggerFactory.Creations}:{LoggedStepMethods.Attempts}:{exception.Message}");
                    }
                }
            }
            """);

        await Assert.That(value).IsEqualTo("1:0:logger");
    }

    [Test]
    public async Task MissingLoggerFactoryFailsBeforeTheFirstAttempt()
    {
        var value = await Run("""
            internal static class LoggedStepMethods
            {
                public static int Attempts;
                [Step]
                internal static void Logged(
                    [FromServices] ILogger logger,
                    CancellationToken token) => Attempts++;
            }
            public static partial class MissingLogging
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    steps.Logged().WithRetry(2);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var services = new ServiceCollection().BuildServiceProvider();
                    try
                    {
                        new MissingLogging.ConfigurationPipeline(services).Execute();
                        return Task.FromResult("unexpected");
                    }
                    catch (InvalidOperationException)
                    {
                        return Task.FromResult(LoggedStepMethods.Attempts.ToString());
                    }
                }
            }
            """);

        await Assert.That(value).IsEqualTo("0");
    }

    [Test]
    public async Task CompositeNodeCanDependOnAnotherNodeWithoutDataTransport()
    {
        var value = await Run("""
            public sealed class State
            {
                public bool Ready;
                public bool Observed;
            }
            internal static class PrepareStepMethods
            {
                [Step]
                internal static void Prepare([FromServices] State state, CancellationToken token) => state.Ready = true;
            }
            internal static class ObserveStepMethods
            {
                [Step]
                internal static void Observe([FromServices] State state, CancellationToken token) => state.Observed = state.Ready;
            }
            public static partial class Ordered
            {
                [Pipeline]
                public static void Configuration(StepGraph steps)
                {
                    var prepare = steps.Prepare();
                    steps.Observe().DependsOn(prepare);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var state = new State();
                    var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
                    new Ordered.ConfigurationPipeline(services).Execute();
                    return Task.FromResult(state.Observed.ToString());
                }
            }
            """);

        await Assert.That(value).IsEqualTo("True");
    }

    [Test]
    public async Task NestedCompositePreservesFailureAndDrainsStartedSibling()
    {
        var value = await Run("""
            public sealed class State
            {
                public Exception Expected = new InvalidOperationException("expected");
                public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public int Cleaned;
            }
            internal static class SlowStepMethods
            {
                [Step]
                internal static Task<int> Slow(State state, CancellationToken token) => Run(state, token);
                private static async Task<int> Run(State state, CancellationToken token)
                {
                    state.Started.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        return 0;
                    }
                    finally
                    {
                        Interlocked.Increment(ref state.Cleaned);
                    }
                }
            }
            internal static class FailStepMethods
            {
                [Step]
                internal static Task<int> Fail(State state, CancellationToken token) => Run(state, token);
                private static async Task<int> Run(State state, CancellationToken token)
                {
                    await state.Started.Task;
                    throw state.Expected;
                }
            }
            public static partial class InnerFailure
            {
                [Pipeline]
                public static void Build(StepGraph steps, State state)
                {
                    var slow = steps.Slow(state);
                    var failure = steps.Fail(state);
                }
            }
            public static partial class OuterFailure
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, State state)
                {
                    var inner = steps.Build(state);
                }
            }
            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var state = new State();
                    try
                    {
                        await new OuterFailure.ConfigurationPipeline().ExecuteAsync(state);
                        return "unexpected";
                    }
                    catch (Exception failure)
                    {
                        return $"{ReferenceEquals(failure, state.Expected)}:{state.Cleaned}";
                    }
                }
            }
            """);

        await Assert.That(value).IsEqualTo("True:1");
    }

    [Test]
    public async Task CallerCancellationFlowsThroughNestedCompositeAndWaitsForCleanup()
    {
        var value = await Run("""
            public sealed class State
            {
                public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public int Cleaned;
            }
            internal static class WaitStepMethods
            {
                [Step]
                internal static Task<int> Wait(State state, CancellationToken token) => Run(state, token);
                private static async Task<int> Run(State state, CancellationToken token)
                {
                    state.Started.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        return 0;
                    }
                    finally
                    {
                        Interlocked.Increment(ref state.Cleaned);
                    }
                }
            }
            public static partial class InnerCancellation
            {
                [Pipeline]
                public static void Build(StepGraph steps, State state)
                {
                    var waiting = steps.Wait(state);
                }
            }
            public static partial class OuterCancellation
            {
                [Pipeline]
                public static void Configuration(StepGraph steps, State state)
                {
                    var inner = steps.Build(state);
                }
            }
            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var state = new State();
                    using var cancellation = new CancellationTokenSource();
                    var execution = new OuterCancellation.ConfigurationPipeline().ExecuteAsync(state, cancellation.Token);
                    await state.Started.Task;
                    cancellation.Cancel();
                    try
                    {
                        await execution;
                        return "unexpected";
                    }
                    catch (OperationCanceledException failure)
                    {
                        return $"{failure.CancellationToken == cancellation.Token}:{state.Cleaned}";
                    }
                }
            }
            """);

        await Assert.That(value).IsEqualTo("True:1");
    }

    [Test]
    public async Task InvalidFunctionLeafDoesNotReceiveGeneratedContext()
    {
        var generated = await Generate("""
            internal static partial class InvalidLeaf
            {
                [Step]
                private static void Run(CancellationToken token) { }
            }
            """);

        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "TTP013")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("required string DisplayName")).IsFalse();
        await Assert.That(generated.GeneratedSource.Contains("partial class InvalidLeaf")).IsFalse();
    }
}
