# Pipeline benchmarks

Measure [TedToolkit.Orchestration.Pipeline](../../README.md) against handwritten code and the repository's pinned alternatives.

**Latest:** [library comparison after immediate retry-exhaustion propagation](retry-exhaustion-benchmark.md). Includes timings, allocations, sampling conditions and limits. The [preceding cleanup run](readability-library-comparison.md) remains available for comparison.

## Reproduce

Run from the repository root with the .NET 10 SDK:

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*ChainBenchmarks*' '*DiamondBenchmarks*' '*ValueSyncBenchmarks*' '*ValueAsyncBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/readability-library-comparison
```

Verification checks every library/graph/mode with repeated inputs and validates synchronous and mixed pipelines. BenchmarkDotNet times separate child processes. Stop builds and CPU-heavy work during measurement. The benchmark project is outside the main solution and package; its external dependencies are not TedPipeline runtime dependencies.

## Workloads

| Group | Work | Compared implementations |
| --- | --- | --- |
| Chain | Four additions, returning the final value | Handwritten, TedPipeline, WorkflowFramework typed pipeline, PipelineNet, TPL Dataflow |
| Diamond | Shared source, two independent operations, then a join | Handwritten, TedPipeline, WorkflowFramework parallel workflow, TPL Dataflow |
| ValueSync | Four synchronous additions and a result sink | Handwritten and generated completion-only execution |
| ValueAsync | Four asynchronous additions and a synchronous sink | Handwritten and generated completion-only execution |

Completed uses already completed Tasks. Yield suspends with Task.Yield and includes scheduling overhead. Completed Tasks are not synchronous Step contracts.

Inputs stay outside the small Task<int> result cache. Execution measurements exclude construction, DI setup, source generation, retries, finite timeouts and failures. All operations finish before returning. Dataflow has one message in flight, so this measures request latency rather than streaming throughput or backpressure.

Handwritten code is a small successful-path baseline; it does not reproduce every cancellation/failure rule or TedPipeline's intermediate result snapshot. Other adapters use their native runtime APIs. Inspect the report before drawing conclusions.

## Additional studies

- `--filter '*ConstructionBenchmarks*'` measures construction separately; it is not included in the latest table.
- [Completed Task access](completed-task-results.md) compares await and GetAwaiter().GetResult() after completion is known.
- [Task reuse optimization](cancellation-optimization.md) records the earlier before/after optimization.
- [Previous library comparison](current-library-comparison.md) predates cancellation simplification.
- [Historical notes and raw measurements](historical-results.md) preserve earlier APIs and should not rank current implementations.
