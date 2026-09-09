using Microsoft.CodeAnalysis;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    public async Task CompositeStepRunsAsRootThroughGeneratedPipeline()
    {
        var value = await Run("""
            internal readonly ref partial struct Add(int left, int right) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(left + right);
            }

            [CompositeStep]
            public readonly ref partial struct Sum(int left, int right)
            {
                private void Configuration(StepGraph steps)
                {
                    var total = steps.Add(left, right);
                }
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var pipeline = new Sum.Pipeline();
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
            internal readonly ref partial struct Value : IStep<int>
            {
                public int Execute(CancellationToken token) => 42;
            }
            [CompositeStep]
            public readonly ref partial struct ServiceFree
            {
                private void Configuration(StepGraph steps)
                {
                    var value = steps.Value();
                }
            }
            """);
        await NoErrors(serviceFree);

        var serviceBound = await Generate("""
            public sealed class ValueProvider { public int Value => 42; }
            internal readonly ref partial struct Read([FromServices] ValueProvider provider) : IStep<int>
            {
                public int Execute(CancellationToken token) => provider.Value;
            }
            [CompositeStep]
            public readonly ref partial struct ServiceBound
            {
                private void Configuration(StepGraph steps)
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
            internal readonly ref partial struct AddOne(int value) : IStep<int>
            {
                public int Execute(CancellationToken token) => value + 1;
            }
            [CompositeStep]
            public readonly ref partial struct Increment(int value)
            {
                private void Configuration(StepGraph steps)
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
            [CompositeStep]
            public readonly ref partial struct Consumer(int value)
            {
                private void Configuration(StepGraph steps)
                {
                    var increment = steps.Increment(value);
                }
            }
            """, MetadataReference.CreateFromImage(image.ToArray()), "CompositeConsumer");
        await NoErrors(consumer);
        await Assert.That(consumer.GeneratedSource).Contains("new global::Increment(");
    }

    [Test]
    public async Task SameNamedCompositesBindByExactGeneratedFactorySymbol()
    {
        var value = await Run("""
            namespace Alpha
            {
                internal readonly ref partial struct AddOne(int value) : IStep<int>
                {
                    public int Execute(CancellationToken token) => value + 1;
                }
                [CompositeStep]
                public readonly ref partial struct Transform(int value)
                {
                    private void Configuration(StepGraph steps)
                    {
                        var number = steps.AddOne(value);
                    }
                }
            }
            namespace Beta
            {
                internal readonly ref partial struct Append(string value) : IStep<string>
                {
                    public string Execute(CancellationToken token) => value + "!";
                }
                [CompositeStep]
                public readonly ref partial struct Transform(string value)
                {
                    private void Configuration(StepGraph steps)
                    {
                        var text = steps.Append(value);
                    }
                }
            }
            [CompositeStep]
            public readonly ref partial struct Both(int value, string text)
            {
                private void Configuration(StepGraph steps)
                {
                    var number = steps.Transform(value);
                    var textValue = steps.Transform(text);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Both.Pipeline().Execute(1, "x");
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
                internal readonly ref partial struct Increment(int value) : IStep<int>
                {
                    public int Execute(CancellationToken token) => value + 1;
                }
            }
            namespace A.B
            {
                internal readonly ref partial struct Increment(int value) : IStep<int>
                {
                    public int Execute(CancellationToken token) => value + 2;
                }
            }
            [CompositeStep]
            public readonly ref partial struct Both(int value)
            {
                private void Configuration(StepGraph steps)
                {
                    var first = A__B_IncrementExtensions.Increment(steps, value);
                    var second = A_B_IncrementExtensions.Increment(steps, value);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Both.Pipeline().Execute(1);
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
            [CompositeStep]
            public readonly ref partial struct First
            {
                private void Configuration(StepGraph steps) { steps.Second(); }
            }
            [CompositeStep]
            public readonly ref partial struct Second
            {
                private void Configuration(StepGraph steps) { steps.First(); }
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
                internal readonly ref partial struct AddOne(int value) : IStep<int>
                {
                    public int Execute(CancellationToken token) => value + 1;
                }
                [CompositeStep]
                public readonly ref partial struct Increment(int value)
                {
                    private void Configuration(StepGraph steps)
                    {
                        var output = steps.AddOne(value);
                    }
                }
            }
            """, assemblyName: "AlphaCompositeLibrary");
        var beta = await Generate("""
            namespace Beta
            {
                internal readonly ref partial struct Double(int value) : IStep<int>
                {
                    public int Execute(CancellationToken token) => value * 2;
                }
                [CompositeStep]
                public readonly ref partial struct Increment(int value)
                {
                    private void Configuration(StepGraph steps)
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
            [CompositeStep]
            public readonly ref partial struct Both(int value)
            {
                private void Configuration(StepGraph steps)
                {
                    var first = Alpha_IncrementExtensions.Increment(steps, value);
                    var second = Beta_IncrementExtensions.Increment(steps, value);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Both.Pipeline().Execute(21);
                    return Task.FromResult($"{result.First.Output}:{result.Second.Output}");
                }
            }
            """, "CompositeConsumer", MetadataReference.CreateFromImage(alphaImage),
            MetadataReference.CreateFromImage(betaImage));
        await NoErrors(consumer);
        await Assert.That(consumer.GeneratedSource).Contains("new global::Alpha.Increment(");
        await Assert.That(consumer.GeneratedSource).Contains("new global::Beta.Increment(");

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

            internal readonly ref partial struct AddOffset(
                int value,
                [FromServices] IOffset offset) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) =>
                    Task.FromResult(value + offset.Value);
            }

            [CompositeStep]
            public readonly ref partial struct Inner(int value)
            {
                private void Configuration(StepGraph steps)
                {
                    var adjusted = steps.AddOffset(value);
                }
            }

            [CompositeStep]
            public readonly ref partial struct Outer(int value)
            {
                private void Configuration(StepGraph steps)
                {
                    var inner = steps.Inner(value);
                }
            }

            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var services = new ServiceCollection()
                        .AddSingleton<IOffset, Offset>()
                        .BuildServiceProvider();
                    var pipeline = new Outer.Pipeline(services);
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
            internal readonly ref partial struct Eventually : IStep<int>
            {
                public static int Attempts;
                public int Execute(CancellationToken token)
                {
                    if (++Attempts <= 2) throw new InvalidOperationException("retry");
                    return 42;
                }
            }
            [CompositeStep]
            public readonly ref partial struct InnerRetry
            {
                private void Configuration(StepGraph steps)
                {
                    var value = steps.Eventually().WithRetry(1);
                }
            }
            [CompositeStep]
            public readonly ref partial struct OuterRetry
            {
                private void Configuration(StepGraph steps)
                {
                    var inner = steps.InnerRetry().WithRetry(1);
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new OuterRetry.Pipeline().Execute();
                    return Task.FromResult($"{result.Inner.Value}:{Eventually.Attempts}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42:3");
    }

    [Test]
    public async Task GeneratedContextCompilesWithoutCs0282()
    {
        var generated = await Generate("""
            internal readonly ref partial struct ReadName(int value) : IStep<string>
            {
                public string Execute(CancellationToken token) => $"{DisplayName}:{value}";
            }

            [CompositeStep]
            public readonly ref partial struct Names(int value)
            {
                private void Configuration(StepGraph steps)
                {
                    var readable = steps.ReadName(value).WithDisplayName("Readable");
                }
            }
            """);

        await NoErrors(generated);
        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "CS0282")).IsFalse();
        await Assert.That(generated.GeneratedSource).Contains("required string DisplayName");
    }

    [Test]
    public async Task GeneratedContextHintNamesAreCollisionResistant()
    {
        var value = await Run("""
            namespace A_B
            {
                internal readonly ref partial struct C : IStep<string>
                {
                    public string Execute(CancellationToken token) => DisplayName;
                }
            }
            namespace A
            {
                internal readonly ref partial struct B_C : IStep<string>
                {
                    public string Execute(CancellationToken token) => DisplayName;
                }
            }
            [CompositeStep]
            public readonly ref partial struct Both
            {
                private void Configuration(StepGraph steps)
                {
                    var first = steps.C();
                    var second = steps.B_C();
                }
            }
            public static class Scenario
            {
                public static Task<string> Run()
                {
                    var result = new Both.Pipeline().Execute();
                    return Task.FromResult($"{result.First}:{result.Second}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("first:second");
    }

    [Test]
    public async Task GeneratedContextHintNamesAreReadableAndDisambiguateOnlyRealCollisions()
    {
        var generated = await Generate("""
            namespace A_B
            {
                internal readonly ref partial struct C : IStep { public void Execute(CancellationToken token) { } }
            }
            namespace A
            {
                internal readonly ref partial struct B_C : IStep { public void Execute(CancellationToken token) { } }
            }
            namespace Demo
            {
                internal readonly ref partial struct e : IStep { public void Execute(CancellationToken token) { } }
                internal readonly ref partial struct e\u0301 : IStep { public void Execute(CancellationToken token) { } }
            }
            namespace @class
            {
                [CompositeStep]
                public readonly ref partial struct @event
                {
                    private void Configuration(StepGraph steps) { }
                }
            }
            """);
        await NoErrors(generated);

        var hintNames = generated.Compilation.SyntaxTrees
            .Select(tree => Path.GetFileName(tree.FilePath))
            .Where(path => path.EndsWith(".StepContext.g.cs", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(hintNames.Length).IsEqualTo(5);
        await Assert.That(hintNames.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(5);
        await Assert.That(hintNames).Contains("A_B.C.StepContext.g.cs");
        await Assert.That(hintNames).Contains("A.B_C.StepContext.g.cs");
        await Assert.That(hintNames).Contains("class.event.StepContext.g.cs");
        await Assert.That(hintNames.Count(name => name.StartsWith("Demo.e", StringComparison.Ordinal))).IsEqualTo(2);
        await Assert.That(hintNames.Any(name => System.Text.RegularExpressions.Regex.IsMatch(
            name, @"\.[0-9a-f]{64}\.StepContext\.g\.cs$"))).IsFalse();
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

            [StepLogger]
            internal readonly ref partial struct Work : IStep<string>
            {
                private static int attempts;
                public string Execute(CancellationToken token)
                {
                    Logger.LogInformation("attempt");
                    if (++attempts == 1) throw new InvalidOperationException("retry");
                    return DisplayName;
                }
            }

            [CompositeStep]
            public readonly ref partial struct LoggedWork
            {
                private void Configuration(StepGraph steps)
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
                    var result = new LoggedWork.Pipeline(services).Execute();
                    return Task.FromResult($"{result.Work}:{CaptureLoggerFactory.Category}:{CaptureLoggerFactory.Creations}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("Friendly:Work[Friendly]:1");
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
            internal readonly ref partial struct Touch : IStep<int>
            {
                public static int Attempts;
                public int Execute(CancellationToken token) { Attempts++; return 42; }
            }
            [StepLogger]
            [CompositeStep]
            public readonly ref partial struct LoggedRoot
            {
                private void Configuration(StepGraph steps)
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
                    var result = new LoggedRoot.Pipeline(services).Execute();
                    return Task.FromResult($"{result.Value}:{CaptureLoggerFactory.Category}:{CaptureLoggerFactory.Creations}:{Touch.Attempts}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42:LoggedRoot[LoggedRoot]:1:1");
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
            internal readonly ref partial struct Eventually : IStep<int>
            {
                public static int Attempts;
                public int Execute(CancellationToken token)
                {
                    if (++Attempts == 1) throw new InvalidOperationException("retry");
                    return 42;
                }
            }
            [StepLogger]
            [CompositeStep]
            public readonly ref partial struct LoggedInner
            {
                private void Configuration(StepGraph steps)
                {
                    var value = steps.Eventually();
                }
            }
            [CompositeStep]
            public readonly ref partial struct LoggedOuter
            {
                private void Configuration(StepGraph steps)
                {
                    var inner = steps.LoggedInner()
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
                    var result = new LoggedOuter.Pipeline(services).Execute();
                    return Task.FromResult($"{result.Inner.Value}:{CaptureLoggerFactory.Category}:{CaptureLoggerFactory.Creations}:{Eventually.Attempts}");
                }
            }
            """);

        await Assert.That(value).IsEqualTo("42:LoggedInner[Friendly Composite]:1:2");
    }

    [Test]
    public async Task MissingRootCompositeLoggerFailsBeforeTheFirstChildAttempt()
    {
        var value = await Run("""
            internal readonly ref partial struct Touch : IStep
            {
                public static int Attempts;
                public void Execute(CancellationToken token) => Attempts++;
            }
            [StepLogger]
            [CompositeStep]
            public readonly ref partial struct LoggedRoot
            {
                private void Configuration(StepGraph steps)
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
                        new LoggedRoot.Pipeline(services).ExecuteWithoutResults();
                        return Task.FromResult("unexpected");
                    }
                    catch (InvalidOperationException)
                    {
                        return Task.FromResult(Touch.Attempts.ToString());
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
            [StepLogger]
            internal readonly ref partial struct Logged : IStep
            {
                public static int Attempts;
                public void Execute(CancellationToken token) => Attempts++;
            }
            [CompositeStep]
            public readonly ref partial struct LoggingFailure
            {
                private void Configuration(StepGraph steps)
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
                        new LoggingFailure.Pipeline(services).ExecuteWithoutResults();
                        return Task.FromResult("unexpected");
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Task.FromResult($"{ThrowingLoggerFactory.Creations}:{Logged.Attempts}:{exception.Message}");
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
            [StepLogger]
            internal readonly ref partial struct Logged : IStep
            {
                public static int Attempts;
                public void Execute(CancellationToken token) => Attempts++;
            }
            [CompositeStep]
            public readonly ref partial struct MissingLogging
            {
                private void Configuration(StepGraph steps)
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
                        new MissingLogging.Pipeline(services).ExecuteWithoutResults();
                        return Task.FromResult("unexpected");
                    }
                    catch (InvalidOperationException)
                    {
                        return Task.FromResult(Logged.Attempts.ToString());
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
            internal readonly ref partial struct Prepare([FromServices] State state) : IStep
            {
                public void Execute(CancellationToken token) => state.Ready = true;
            }
            internal readonly ref partial struct Observe([FromServices] State state) : IStep
            {
                public void Execute(CancellationToken token) => state.Observed = state.Ready;
            }
            [CompositeStep]
            public readonly ref partial struct Ordered
            {
                private void Configuration(StepGraph steps)
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
                    new Ordered.Pipeline(services).ExecuteWithoutResults();
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
            internal readonly ref partial struct Slow(State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => Run(state, token);
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
            internal readonly ref partial struct Fail(State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => Run(state, token);
                private static async Task<int> Run(State state, CancellationToken token)
                {
                    await state.Started.Task;
                    throw state.Expected;
                }
            }
            [CompositeStep]
            public readonly ref partial struct InnerFailure(State state)
            {
                private void Configuration(StepGraph steps)
                {
                    var slow = steps.Slow(state);
                    var failure = steps.Fail(state);
                }
            }
            [CompositeStep]
            public readonly ref partial struct OuterFailure(State state)
            {
                private void Configuration(StepGraph steps)
                {
                    var inner = steps.InnerFailure(state);
                }
            }
            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var state = new State();
                    try
                    {
                        await new OuterFailure.Pipeline().ExecuteAsync(state);
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
            internal readonly ref partial struct Wait(State state) : IAsyncStep<int>
            {
                public Task<int> ExecuteAsync(CancellationToken token) => Run(state, token);
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
            [CompositeStep]
            public readonly ref partial struct InnerCancellation(State state)
            {
                private void Configuration(StepGraph steps)
                {
                    var waiting = steps.Wait(state);
                }
            }
            [CompositeStep]
            public readonly ref partial struct OuterCancellation(State state)
            {
                private void Configuration(StepGraph steps)
                {
                    var inner = steps.InnerCancellation(state);
                }
            }
            public static class Scenario
            {
                public static async Task<string> Run()
                {
                    var state = new State();
                    using var cancellation = new CancellationTokenSource();
                    var execution = new OuterCancellation.Pipeline().ExecuteAsync(state, cancellation.Token);
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
    [Arguments("public Invalid(int value) { }")]
    [Arguments("public Invalid() { }")]
    [Arguments("public Invalid() { } public Invalid(int value) { }")]
    public async Task CompositeOrdinaryConstructorsAreRejected(string constructors)
    {
        var generated = await Generate("""
            [CompositeStep]
            public readonly ref partial struct Invalid
            {
                CONSTRUCTORS
                private void Configuration(StepGraph steps) { }
            }
            """.Replace("CONSTRUCTORS", constructors));

        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "TTP009")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("required string DisplayName")).IsFalse();
    }

    [Test]
    public async Task CompositeWithoutConfigurationIsRejectedAndCs0282IsNotSuppressed()
    {
        var generated = await Generate("""
            [CompositeStep]
            public readonly ref partial struct MissingConfiguration
            {
                private readonly int first;
            }
            public readonly ref partial struct MissingConfiguration
            {
                private readonly int second;
            }
            """);

        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "TTP009")).IsTrue();
        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "CS0282")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("required string DisplayName")).IsFalse();
    }

    [Test]
    public async Task InvalidMultiContractLeafDoesNotReceiveContextOrCs0282Suppression()
    {
        var generated = await Generate("""
            internal readonly ref partial struct InvalidLeaf : IStep, IAsyncStep
            {
                private readonly int first;
                public void Execute(CancellationToken token) { }
                public Task ExecuteAsync(CancellationToken token) => Task.CompletedTask;
            }
            internal readonly ref partial struct InvalidLeaf
            {
                private readonly int second;
            }
            """);

        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "TTP013")).IsTrue();
        await Assert.That(generated.Diagnostics.Any(diagnostic => diagnostic.Id == "CS0282")).IsTrue();
        await Assert.That(generated.GeneratedSource.Contains("required string DisplayName")).IsFalse();
    }
}
