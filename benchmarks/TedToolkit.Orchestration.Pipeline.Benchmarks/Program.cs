using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Perfolizer.Horology;
using TedToolkit.Orchestration.Pipeline.Benchmarks;

if (args.Contains("--verify"))
{
    foreach (var mode in Enum.GetValues<WorkMode>())
        foreach (var diamond in new[] { false, true })
        {
            var runners = new List<IRunner> { new DirectRunner(mode, diamond), new TedRunner(mode, diamond), new DataflowRunner(mode, diamond) };
            if (diamond) runners.Add(new WorkflowDiamondRunner(mode));
            else { runners.Add(new WorkflowPipelineRunner(mode)); runners.Add(new PipelineNetRunner(mode)); }
            foreach (var runner in runners)
            {
                await using (runner)
                    foreach (var input in new[] { 1024, 2048, 1024 })
                    {
                        var actual = await runner.RunAsync(input).WaitAsync(TimeSpan.FromSeconds(10));
                        var expected = diamond ? input * 2 + 5 : input + 4;
                        if (actual != expected) throw new InvalidOperationException($"{runner.GetType().Name}: {actual} != {expected}");
                    }
                Console.WriteLine($"PASS {runner.GetType().Name}, {mode}, {(diamond ? "diamond" : "chain")}");
            }
        }
    new ValueSyncBenchmarks().Setup();
    Console.WriteLine("PASS synchronous generated and handwritten execution");
    foreach (var mode in Enum.GetValues<WorkMode>())
    {
        await new ValueAsyncBenchmarks { Mode = mode }.Setup();
        Console.WriteLine($"PASS mixed generated and handwritten execution, {mode}");
        await new CompositeNestingBenchmarks { Mode = mode }.Verify();
        Console.WriteLine($"PASS flat and nested Composite execution, {mode}");
    }
    new CompositeSyncNestingBenchmarks().Verify();
    Console.WriteLine("PASS synchronous flat and nested Composite execution");
    return;
}
var config = DefaultConfig.Instance
    .AddJob(Job.Default.WithId("Local").WithLaunchCount(1).WithWarmupCount(5).WithIterationCount(10)
        .WithIterationTime(TimeInterval.FromMilliseconds(250)))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full)
    .WithArtifactsPath("artifacts/benchmarks");
BenchmarkSwitcher.FromAssembly(typeof(ChainBenchmarks).Assembly).Run(args, config);
