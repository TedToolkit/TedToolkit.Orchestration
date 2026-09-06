# Pipeline benchmarks

This project measures the request-execution cost of [TedToolkit.Orchestration.Pipeline](../../README.md) against equivalent handwritten tasks and pinned neighboring libraries. It exists to test the compile-time design with reproducible evidence, not to produce a universal framework ranking.

The latest complete snapshot is [the 2026-09-05 comparison after retry-exhaustion cleanup](retry-exhaustion-benchmark.md). It records the machine, runtime, package versions, confidence intervals, allocations, adapter verification, and interpretation limits.

## What is compared

| Adapter | Version/source | Why it is present |
| --- | --- | --- |
| Handwritten tasks | Repository benchmark code | Lower-abstraction baseline for the matched successful payload |
| TedPipeline | Current workspace | Generated static dependency graph |
| WorkflowFramework | 1.0.10 | Neighboring typed pipeline/workflow approach |
| PipelineNet | 0.11.0 | Middleware-style runtime pipeline |
| TPL Dataflow | .NET 10 | Runtime blocks with streaming and backpressure capabilities |

These adapters do not have identical product boundaries. The benchmark matches the small arithmetic payload and waits for all operations, but it does not make handwritten code reproduce all TedPipeline cancellation/failure semantics, and it does not exercise Dataflow streaming throughput or backpressure.

## Latest result summary

Environment: BenchmarkDotNet 0.15.8, .NET 10.0.11 x64, Windows 11 25H2, Intel Core i7-12700H; two launches, five warmups, 15 measured iterations, 250 ms target iteration time.

### Suspended asynchronous work

Each operation suspends with `Task.Yield`.

| Implementation | Chain mean / allocation | Diamond mean / allocation |
| --- | ---: | ---: |
| Handwritten tasks | 3.44 μs / 560 B | 5.13 μs / 720 B |
| TedPipeline | 3.36 μs / 688 B | 4.33 μs / 1,697 B |
| WorkflowFramework | 3.47 μs / 1,032 B | 8.05 μs / 4,429 B |
| PipelineNet | 10.46 μs / 2,878 B | Not represented |
| TPL Dataflow | 13.65 μs / 2,173 B | 14.45 μs / 2,318 B |

The chain intervals overlap, so the small timing differences among handwritten tasks, TedPipeline, and WorkflowFramework are inconclusive. The TedPipeline diamond mean is lower than handwritten in this run, but the handwritten interval is wide and overlaps; the result does not prove a general timing advantage.

### Already-completed tasks

| Implementation | Chain mean / allocation | Diamond mean / allocation |
| --- | ---: | ---: |
| Handwritten tasks | 47.77 ns / 360 B | 128.41 ns / 520 B |
| TedPipeline | 79.48 ns / 440 B | 257.24 ns / 848 B |
| WorkflowFramework | 113.13 ns / 648 B | 1,387.53 ns / 2,408 B |
| PipelineNet | 423.27 ns / 808 B | Not represented |
| TPL Dataflow | 9,683.76 ns / 2,033 B | 14,567.68 ns / 2,184 B |

Completed tasks are still asynchronous step contracts; they are not the same as synchronous steps.

### Completion-only execution

| Workload | Handwritten | Generated |
| --- | ---: | ---: |
| Four synchronous additions | 2.54 ns / 0 B | 11.95 ns / 0 B |
| Mixed steps, completed tasks | 46.30 ns / 288 B | 63.79 ns / 288 B |
| Mixed steps, yielding tasks | 3.42 μs / 536 B | 3.51 μs / 536 B |

This is the clearest trade-off: handwritten synchronous code remains cheaper, while generated completion-only execution avoids allocation and the suspended mixed path is close to handwritten in this workload.

## Workloads

| Group | Work | Included adapters |
| --- | --- | --- |
| Chain | Four additions in sequence, returning the final value | All adapters |
| Diamond | Shared source, two independent operations, then a join | Handwritten, TedPipeline, WorkflowFramework, TPL Dataflow |
| ValueSync | Four synchronous additions and a result sink | Handwritten and generated completion-only execution |
| ValueAsync | Four asynchronous additions and a synchronous sink | Handwritten and generated completion-only execution |
| Construction | Native adapter/executor construction | TedPipeline, WorkflowFramework, PipelineNet |

Inputs stay outside the small `Task<int>` result cache. Execution measurements exclude construction, DI setup, source generation, retries, finite timeouts, and failures unless a focused study explicitly says otherwise.

## Correctness gate

Run adapter verification before collecting timings:

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify
```

Verification runs every library/shape/mode combination with repeated inputs and also checks the synchronous and mixed generated paths. It catches wrong results and retained state; it does not prove feature equivalence outside the measured payload.

## Reproduce the latest comparison

From the repository root with the .NET 10 SDK:

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*ChainBenchmarks*' '*DiamondBenchmarks*' '*ValueSyncBenchmarks*' '*ValueAsyncBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/readability-library-comparison
```

BenchmarkDotNet executes timed cases in separate child processes. Stop other builds and CPU-heavy work before measuring. No processor affinity was imposed on the recorded run, so compare confidence intervals and distributions rather than isolated means.

## How to interpret the result

- Choose TedPipeline when a graph is static and generated typed wiring, shared policies, and compile-time diagnostics remove useful coordination code.
- Choose handwritten code when the flow is small and minimum orchestration overhead matters more than reusable semantics.
- Choose another runtime pipeline/workflow library when its configuration model or broader workflow features match the application.
- Choose Dataflow when streaming, buffering, backpressure, or multiple messages in flight are requirements; this benchmark measures none of those strengths.
- Do not compare numbers across separate sessions as if only the implementation changed. Absolute handwritten baselines also move with the host and run conditions.

## Evidence and study history

| Study | Purpose |
| --- | --- |
| [Latest comparison](retry-exhaustion-benchmark.md) | Current full tables and interpretation after retry-exhaustion cleanup |
| [Preceding readability comparison](readability-library-comparison.md) | Earlier comparable library run |
| [Completed-task access](completed-task-results.md) | `await` versus `GetAwaiter().GetResult()` after completion is known |
| [Cancellation/task reuse optimization](cancellation-optimization.md) | Focused before/after optimization evidence |
| [Earlier library comparison](current-library-comparison.md) | Snapshot before later cancellation simplification |
| [Historical results](historical-results.md) | Older APIs and experiments; not current ranking evidence |

Generated output belongs under `artifacts/` or BenchmarkDotNet's artifact directory and is not part of the runtime package. The benchmark project's external dependencies are benchmark-only and do not become TedPipeline runtime dependencies.
