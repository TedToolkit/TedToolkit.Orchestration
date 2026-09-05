using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.Orchestration.Pipeline.Analyzer;

namespace TedToolkit.Orchestration.Pipeline.Tests;

public partial class ExecutorGeneratorTests
{
    private const string Imports = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using TedToolkit.Orchestration.Pipeline;
        using TedToolkit.Orchestration.Pipeline.Attributes;

        """;
    private const string SimpleSteps = """
        public record Inputs(int Value);
        internal readonly ref struct AddStep(int a, int b) : IAsyncStep<int>
        {
            public Task<int> ExecuteAsync(CancellationToken token) => Task.FromResult(a + b);
        }
        """;
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(IAsyncStep<>).Assembly.Location).Append(typeof(ServiceCollection).Assembly.Location)
        .Append(typeof(IServiceCollection).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    private static string Scenario(string body) => "public static class Scenario { public static Task<string> Run() { " + body + " } }";
    private static string AsyncScenario(string body) => "public static class Scenario { public static async Task<string> Run() { " + body + " } }";
    private static CSharpParseOptions ParseOptions => new CSharpParseOptions(LanguageVersion.Preview);

    private static async Task<GeneratedCompilation> Generate(string source, MetadataReference? additionalReference = null, string? assemblyName = null)
    {
        var compilation = CSharpCompilation.Create(assemblyName ?? "PipelineScenario_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(Imports + source, ParseOptions, "Scenario.cs")],
            additionalReference is null ? References : References.Add(additionalReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, optimizationLevel: OptimizationLevel.Release));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new PipelineExecutorGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var generatorDiagnostics);
        var analyzerDiagnostics = await generated.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineAnalyzer())).GetAnalyzerDiagnosticsAsync();
        return new GeneratedCompilation(generated, generatorDiagnostics.AddRange(analyzerDiagnostics).AddRange(generated.GetDiagnostics()),
            string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString())));
    }

    private static Task NoErrors(GeneratedCompilation generated)
    {
        var errors = string.Join("\n", generated.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8785"));
        if (errors.Length != 0) throw new InvalidOperationException(errors);
        return Task.CompletedTask;
    }

    private static async Task<string> Run(string source) =>
        await (await Compile<Func<Task<string>>>(source))().WaitAsync(TimeSpan.FromSeconds(10));

    internal static async Task<TDelegate> Compile<TDelegate>(string source) where TDelegate : Delegate
    {
        var generated = await Generate(source);
        await NoErrors(generated);
        using var stream = new MemoryStream();
        var emitted = generated.Compilation.Emit(stream);
        await Assert.That(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))).IsEqualTo("");
        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        return assembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.CreateDelegate<TDelegate>();
    }

    private sealed record GeneratedCompilation(Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource);
}

