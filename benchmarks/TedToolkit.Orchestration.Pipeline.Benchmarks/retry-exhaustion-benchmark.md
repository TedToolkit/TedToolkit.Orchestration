# Library comparison after immediate retry-exhaustion propagation — 2026-09-05

This snapshot changes terminal retry failures to propagate immediately from HandleFailure instead of waiting for the following Begin call. All **133 tests** passed; adapter verification passed all 18 library/shape/mode cases and three synchronous/mixed checks. The successful execution path and graph model are unchanged.

## Method and limits

BenchmarkDotNet 0.15.8; Windows 11 25H2 build 26200.9168; Intel Core i7-12700H (14 physical / 20 logical cores); .NET SDK 10.0.400; .NET 10.0.11 x64 RyuJIT x86-64-v3, workstation concurrent GC. Two launches, five warmups, 15 measurement iterations per launch, 250 ms target. All 24 cases completed. No other builds or tests ran during timing; no processor affinity was imposed.

Versions are pinned: WorkflowFramework 1.0.10, PipelineNet 0.11.0 and .NET 10 TPL Dataflow. This is not a survey of all available versions or libraries. Error below is half the 99.9% confidence interval. BenchmarkDotNet's outlier and distribution warnings remain in the raw log; overlapping intervals are not evidence of a clear winner. Absolute timings differ from earlier sessions, including the handwritten baselines, so cross-session timing changes must not be attributed solely to refactoring.

Chain and diamond each perform four arithmetic operations. Completed returns completed Tasks; Yield suspends each operation with Task.Yield. ValueSync and ValueAsync perform four additions and a synchronous sink, using completion-only execution. Inputs 1024 and 2048 avoid the small Task<int> cache; verification repeats inputs to catch retained state.

Construction, source generation, DI setup, retries, finite timeouts and failures are excluded. Handwritten code implements the successful payload, not every TedPipeline cancellation/failure rule or intermediate result snapshot. WorkflowFramework uses its native typed pipeline for chains and typed-context parallel workflow for diamonds. PipelineNet resolves middleware per invocation. Dataflow has one message in flight: these measurements do not assess sustained streaming throughput or backpressure. These different framework responsibilities limit feature-equivalence claims.

## Results

### Chain

| Implementation | Mode | Mean ± error | Allocated / operation |
| --- | --- | ---: | ---: |
| DirectTasks | Completed | 47.77 ± 2.05 ns | 360 B |
| TedPipeline | Completed | 79.48 ± 4.48 ns | 440 B |
| WorkflowTypedPipeline | Completed | 113.13 ± 5.22 ns | 648 B |
| PipelineNet | Completed | 423.27 ± 13.81 ns | 808 B |
| TplDataflow | Completed | 9683.76 ± 641.24 ns | 2,033 B |
| DirectTasks | Yield | 3.44 ± 0.07 μs | 560 B |
| TedPipeline | Yield | 3.36 ± 0.13 μs | 688 B |
| WorkflowTypedPipeline | Yield | 3.47 ± 0.16 μs | 1,032 B |
| PipelineNet | Yield | 10.46 ± 2.87 μs | 2,878 B |
| TplDataflow | Yield | 13.65 ± 0.51 μs | 2,173 B |

### Diamond

| Implementation | Mode | Mean ± error | Allocated / operation |
| --- | --- | ---: | ---: |
| DirectTasks | Completed | 128.41 ± 4.67 ns | 520 B |
| TedPipeline | Completed | 257.24 ± 16.70 ns | 848 B |
| WorkflowParallel | Completed | 1387.53 ± 55.33 ns | 2,408 B |
| TplDataflow | Completed | 14567.68 ± 2590.06 ns | 2,184 B |
| DirectTasks | Yield | 5.13 ± 1.57 μs | 720 B |
| TedPipeline | Yield | 4.33 ± 0.11 μs | 1,697 B |
| WorkflowParallel | Yield | 8.05 ± 0.43 μs | 4,429 B |
| TplDataflow | Yield | 14.45 ± 0.81 μs | 2,318 B |

### Synchronous completion only

| Implementation | Mode | Mean ± error | Allocated / operation |
| --- | --- | ---: | ---: |
| Handwritten | Synchronous | 2.54 ± 0.14 ns | 0 B |
| Generated | Synchronous | 11.95 ± 0.59 ns | 0 B |

### Mixed completion only

| Implementation | Mode | Mean ± error | Allocated / operation |
| --- | --- | ---: | ---: |
| Handwritten | Completed | 46.30 ± 1.85 ns | 288 B |
| Generated | Completed | 63.79 ± 2.96 ns | 288 B |
| Handwritten | Yield | 3.42 ± 0.12 μs | 536 B |
| Generated | Yield | 3.51 ± 0.11 μs | 536 B |

## What this demonstrates

- Static generated coordination avoids runtime graph traversal and per-Step interface boxing. Default serial asynchronous execution can reuse business Tasks; pure synchronous execution requires no Tasks. These are implementation properties, not claims about every competitor's internals.
- Yielding chain: TedPipeline averages 0.98× the handwritten baseline, allocating 688 B versus 560 B. Compare the intervals before interpreting a small timing difference.
- Yielding diamond: TedPipeline averages 4.33 μs versus 5.13 μs handwritten, but the handwritten interval is wide and overlaps; this run does not establish a timing win. TedPipeline allocates 1,697 B versus 720 B because it also coordinates cancellation and collects intermediate results.
- Mixed completion-only execution averages 1.03× handwritten with 536 B versus 536 B.
- Relative to the other library adapters, the tables identify where lower latency or fewer allocations are observed. They do not establish a universal ranking. Workflow scheduling and buffering features excluded from this benchmark may be the reason to choose another library.
- Choose TedPipeline when the graph is static and typed wiring, generated orchestration, and shared execution policies save useful code. Choose handwritten code when minimizing orchestration overhead matters more than those conveniences. Durable or distributed workflows and streaming backpressure are outside this library's purpose.

## Reproduction and evidence

Run the commands in the [benchmark README](README.md). The source snapshot is frozen: [hash manifest](results/2026-09-05/retry-exhaustion/source.sha256), [source ZIP](results/2026-09-05/retry-exhaustion/source.zip), [full log](results/2026-09-05/retry-exhaustion/run.log), [test result](results/2026-09-05/retry-exhaustion/tests.log), [adapter verification](results/2026-09-05/retry-exhaustion/adapter-verification.log), and [zero-warning build](results/2026-09-05/retry-exhaustion/build.log).

Original BenchmarkDotNet Markdown, CSV and full JSON (individual measurements) are preserved alongside these files:

- [ChainBenchmarks github.md](results/2026-09-05/retry-exhaustion/TedToolkit.Orchestration.Pipeline.Benchmarks.ChainBenchmarks-report-github.md)
- [ChainBenchmarks full.json](results/2026-09-05/retry-exhaustion/TedToolkit.Orchestration.Pipeline.Benchmarks.ChainBenchmarks-report-full.json)
- [DiamondBenchmarks github.md](results/2026-09-05/retry-exhaustion/TedToolkit.Orchestration.Pipeline.Benchmarks.DiamondBenchmarks-report-github.md)
- [DiamondBenchmarks full.json](results/2026-09-05/retry-exhaustion/TedToolkit.Orchestration.Pipeline.Benchmarks.DiamondBenchmarks-report-full.json)
- [ValueSyncBenchmarks github.md](results/2026-09-05/retry-exhaustion/TedToolkit.Orchestration.Pipeline.Benchmarks.ValueSyncBenchmarks-report-github.md)
- [ValueSyncBenchmarks full.json](results/2026-09-05/retry-exhaustion/TedToolkit.Orchestration.Pipeline.Benchmarks.ValueSyncBenchmarks-report-full.json)
- [ValueAsyncBenchmarks github.md](results/2026-09-05/retry-exhaustion/TedToolkit.Orchestration.Pipeline.Benchmarks.ValueAsyncBenchmarks-report-github.md)
- [ValueAsyncBenchmarks full.json](results/2026-09-05/retry-exhaustion/TedToolkit.Orchestration.Pipeline.Benchmarks.ValueAsyncBenchmarks-report-full.json)
