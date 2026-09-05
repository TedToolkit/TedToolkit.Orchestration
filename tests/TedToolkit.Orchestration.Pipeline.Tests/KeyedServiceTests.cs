namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ServicesResolveExactKeysFromTheCallerScope(bool discardResults)
    {
        var result = await Run("""
            public sealed record Value(string Text);
            public sealed class Sink { public string Text = ""; }
            internal readonly ref struct Read(
                [FromServices] Value ordinary,
                [FromServices(key: null)] Value explicitNull,
                [FromServices(key: "blue")] Value named,
                [FromServices("")] Value empty,
                [FromServices("quote\"\\\n键")] Value escaped) : IStep<string>
            {
                public string Execute(CancellationToken token) =>
                    $"{ordinary.Text}:{explicitNull.Text}:{named.Text}:{empty.Text}:{escaped.Text}";
            }
            internal readonly ref struct Write(string value, [FromServices("sink")] Sink sink) : IAsyncStep
            {
                public Task ExecuteAsync(CancellationToken token) { sink.Text = value; return Task.CompletedTask; }
            }
            public partial class Example : global::TedToolkit.Orchestration.Pipeline.Pipeline
            {
                private void Configure(Builder p) { var read = p.Read(); p.Write(read); }
            }
            """ + AsyncScenario("""
                using var root = new ServiceCollection()
                    .AddScoped(_ => new Value("ordinary"))
                    .AddKeyedScoped<Value>("blue", (_, _) => new Value("named"))
                    .AddKeyedScoped<Value>("", (_, _) => new Value("empty"))
                    .AddKeyedScoped<Value>("quote\"\\\n键", (_, _) => new Value("escaped"))
                    .AddKeyedScoped<Sink>("sink", (_, _) => new Sink())
                    .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                using var scope = root.CreateScope();
                var executor = new Example(scope.ServiceProvider);
                """ + (discardResults ? "await executor.ExecuteWithoutResultsAsync();" : "await executor.ExecuteAsync();") + """
                return scope.ServiceProvider.GetRequiredKeyedService<Sink>("sink").Text;
                """));
        await Assert.That(result).IsEqualTo("ordinary:ordinary:named:empty:escaped");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingKeyedServiceDoesNotFallBackToOrdinaryService(bool discardResults)
    {
        var result = await Run("""
            public sealed class Service;
            internal readonly ref struct Read([FromServices("missing")] Service service) : IStep<bool>
            {
                public bool Execute(CancellationToken token) => service is not null;
            }
            public partial class Example : global::TedToolkit.Orchestration.Pipeline.Pipeline
            {
                private void Configure(Builder p) { p.Read(); }
            }
            """ + AsyncScenario("""
                using var services = new ServiceCollection().AddSingleton(new Service()).BuildServiceProvider();
                var executor = new Example(services);
                try {
                """ + (discardResults ? "executor.ExecuteWithoutResults();" : "executor.Execute();") + """
                } catch (InvalidOperationException) { return "missing"; }
                return "incorrect fallback";
                """));
        await Assert.That(result).IsEqualTo("missing");
    }
}



