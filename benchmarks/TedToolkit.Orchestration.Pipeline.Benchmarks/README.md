# Pipeline benchmarks

This project measures the request-execution cost of [TedToolkit.Orchestration.Pipeline](../../README.md) against equivalent handwritten tasks and pinned neighboring libraries. It exists to test the compile-time design with reproducible evidence, not to produce a universal framework ranking.

The latest focused and complete maintained-comparison snapshot is [the 2026-09-08 Composite Step study](composite-step-results.md). The preceding complete cross-library snapshot is [the 2026-09-05 comparison after retry-exhaustion cleanup](retry-exhaustion-benchmark.md).

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
| Handwritten tasks | 2.850 μs / 560 B | 2.810 μs / 720 B |
| TedPipeline | 2.895 μs / 680 B | 4.362 μs / 1,682 B |
| WorkflowFramework | 3.294 μs / 1,032 B | 8.006 μs / 4,433 B |
| PipelineNet | 5.764 μs / 2,847 B | Not represented |
| TPL Dataflow | 13.109 μs / 2,161 B | 11.800 μs / 2,314 B |

The handwritten and TedPipeline chain intervals overlap, so their small timing difference is inconclusive. The diamond workload exposes additional generated coordination: TedPipeline is slower and allocates 962 B more than the lower-abstraction handwritten baseline in this run.

### Already-completed tasks

| Implementation | Chain mean / allocation | Diamond mean / allocation |
| --- | ---: | ---: |
| Handwritten tasks | 35.76 ns / 360 B | 105.5 ns / 520 B |
| TedPipeline | 60.32 ns / 440 B | 258.3 ns / 848 B |
| WorkflowFramework | 92.71 ns / 648 B | 878.8 ns / 2,408 B |
| PipelineNet | 374.64 ns / 808 B | Not represented |
| TPL Dataflow | 7,957.03 ns / 2,032 B | 9,173.3 ns / 2,185 B |

Completed tasks are still asynchronous step contracts; they are not the same as synchronous steps.

### Completion-only execution

| Workload | Handwritten | Generated |
| --- | ---: | ---: |
| Four synchronous additions | 2.659 ns / 0 B | 11.645 ns / 0 B |
| Mixed steps, completed tasks | 35.97 ns / 288 B | 45.56 ns / 288 B |
| Mixed steps, yielding tasks | 2.980 μs / 536 B | 2.857 μs / 544 B |

This is the clearest trade-off: handwritten synchronous code remains cheaper, while generated completion-only execution avoids allocation and the suspended mixed path is close to handwritten in this workload.

### Composite nesting

The focused study compares an equivalent flat graph with one nested Composite boundary. Pipeline facade construction occurs in setup.

| Path | Flat mean / allocation | Nested mean / allocation |
| --- | ---: | ---: |
| Synchronous | 5.235 ns / 0 B | 6.264 ns / 0 B |
| Completed tasks | 47.79 ns / 288 B | 55.80 ns / 360 B |
| Yielding tasks | 1.793 μs / 440 B | 1.863 μs / 560 B |

The synchronous and yielding flat/nested 99.9% confidence intervals overlap. The completed path adds 8.01 ns and 72 B, within its approved absolute limits of 16 ns and 512 B. The yielding nested path adds 120 B, and the synchronous path remains allocation-free. The synchronous nested distribution was bimodal and the asynchronous run also reported multimodality, so the intervals—not isolated means—are the acceptance evidence.

## Workloads

| Group | Work | Included adapters |
| --- | --- | --- |
| Chain | Four additions in sequence, returning the final value | All adapters |
| Diamond | Shared source, two independent operations, then a join | Handwritten, TedPipeline, WorkflowFramework, TPL Dataflow |
| ValueSync | Four synchronous additions and a result sink | Handwritten and generated completion-only execution |
| ValueAsync | Four asynchronous additions and a synchronous sink | Handwritten and generated completion-only execution |
| Composite | Equivalent two-node flat graph and one-boundary nested graph across sync, completed-task, and yielding paths | Direct, flat generated, and nested generated paths |
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
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*CompositeSyncNestingBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-sync
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*CompositeNestingBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-async
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*ChainBenchmarks*' '*DiamondBenchmarks*' '*ValueSyncBenchmarks*' '*ValueAsyncBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-maintained
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
| [Composite Step nesting](composite-step-results.md) | Current flat-versus-nested sync, completed-task, and yielding evidence |
| [Preceding comparison](retry-exhaustion-benchmark.md) | Earlier full tables and interpretation after retry-exhaustion cleanup |
| [Preceding readability comparison](readability-library-comparison.md) | Earlier comparable library run |
| [Completed-task access](completed-task-results.md) | `await` versus `GetAwaiter().GetResult()` after completion is known |
| [Cancellation/task reuse optimization](cancellation-optimization.md) | Focused before/after optimization evidence |
| [Earlier library comparison](current-library-comparison.md) | Snapshot before later cancellation simplification |
| [Historical results](historical-results.md) | Older APIs and experiments; not current ranking evidence |

Generated output belongs under `artifacts/` or BenchmarkDotNet's artifact directory and is not part of the runtime package. The benchmark project's external dependencies are benchmark-only and do not become TedPipeline runtime dependencies.
