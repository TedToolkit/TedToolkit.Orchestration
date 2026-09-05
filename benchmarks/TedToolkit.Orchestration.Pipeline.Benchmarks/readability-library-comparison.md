# Library comparison after cleanup — 2026-09-05

This snapshot includes shared semantic configuration recognition, separated generator stages, usage diagnostics TTP014–TTP016, direct linked-source cancellation and cancellation-check deduplication. All **128 tests** passed; adapter verification passed all 18 library/shape/mode cases and three synchronous/mixed checks. The cleanup does not introduce a different execution model.

## Method and limits

BenchmarkDotNet 0.15.8; Windows 11 25H2 build 26200.9168; Intel Core i7-12700H (14 physical / 20 logical cores); .NET SDK 10.0.400; .NET 10.0.11 x64 RyuJIT x86-64-v3, workstation concurrent GC. Two launches, five warmups, 15 measurement iterations per launch, 250 ms target. All 24 cases completed. No other builds or tests ran during timing; no processor affinity was imposed.

Versions are pinned: WorkflowFramework 1.0.10, PipelineNet 0.11.0 and .NET 10 TPL Dataflow. This is not a survey of all available versions or libraries. Error below is half the 99.9% confidence interval. BenchmarkDotNet's outlier and distribution warnings remain in the raw log; overlapping intervals are not evidence of a clear winner. Absolute timings differ from earlier sessions, including the handwritten baselines, so cross-session timing changes must not be attributed solely to refactoring.

Chain and diamond each perform four arithmetic operations. Completed returns completed Tasks; Yield suspends each operation with Task.Yield. ValueSync and ValueAsync perform four additions and a synchronous sink, using completion-only execution. Inputs 1024 and 2048 avoid the small Task<int> cache; verification repeats inputs to catch retained state.

Construction, source generation, DI setup, retries, finite timeouts and failures are excluded. Handwritten code implements the successful payload, not every TedPipeline cancellation/failure rule or intermediate result snapshot. WorkflowFramework uses its native typed pipeline for chains and typed-context parallel workflow for diamonds. PipelineNet resolves middleware per invocation. Dataflow has one message in flight: these measurements do not assess sustained streaming throughput or backpressure. These different framework responsibilities limit feature-equivalence claims.

## Results

### Chain

| Implementation | Mode | Mean ± error | Allocated / operation |
| --- | --- | ---: | ---: |
| DirectTasks | Completed | 56.83 ± 2.38 ns | 360 B |
| TedPipeline | Completed | 89.55 ± 3.62 ns | 440 B |
| WorkflowTypedPipeline | Completed | 134.28 ± 7.26 ns | 648 B |
| PipelineNet | Completed | 545.29 ± 36.48 ns | 808 B |
| TplDataflow | Completed | 10393.13 ± 661.85 ns | 2,033 B |
| DirectTasks | Yield | 3.82 ± 0.16 μs | 560 B |
| TedPipeline | Yield | 3.85 ± 0.15 μs | 688 B |
| WorkflowTypedPipeline | Yield | 4.29 ± 0.35 μs | 1,032 B |
| PipelineNet | Yield | 9.41 ± 0.85 μs | 2,881 B |
| TplDataflow | Yield | 15.58 ± 0.77 μs | 2,163 B |

### Diamond

| Implementation | Mode | Mean ± error | Allocated / operation |
| --- | --- | ---: | ---: |
| DirectTasks | Completed | 139.57 ± 4.87 ns | 520 B |
| TedPipeline | Completed | 272.83 ± 15.48 ns | 848 B |
| WorkflowParallel | Completed | 1472.01 ± 154.42 ns | 2,408 B |
| TplDataflow | Completed | 15545.63 ± 694.88 ns | 2,184 B |
| DirectTasks | Yield | 4.10 ± 0.40 μs | 720 B |
| TedPipeline | Yield | 5.73 ± 0.36 μs | 1,693 B |
| WorkflowParallel | Yield | 9.22 ± 0.66 μs | 4,438 B |
| TplDataflow | Yield | 19.62 ± 3.74 μs | 2,318 B |

### Synchronous completion only

| Implementation | Mode | Mean ± error | Allocated / operation |
| --- | --- | ---: | ---: |
| Handwritten | Synchronous | 2.96 ± 0.17 ns | 0 B |
| Generated | Synchronous | 12.76 ± 0.92 ns | 0 B |

### Mixed completion only

| Implementation | Mode | Mean ± error | Allocated / operation |
| --- | --- | ---: | ---: |
| Handwritten | Completed | 42.24 ± 1.91 ns | 288 B |
| Generated | Completed | 61.34 ± 3.32 ns | 288 B |
| Handwritten | Yield | 3.45 ± 0.11 μs | 536 B |
| Generated | Yield | 3.28 ± 0.08 μs | 536 B |

## What this demonstrates

- Static generated coordination avoids runtime graph traversal and per-Step interface boxing. Default serial asynchronous execution can reuse business Tasks; pure synchronous execution requires no Tasks. These are implementation properties, not claims about every competitor's internals.
- Yielding chain: TedPipeline averages 1.01× the handwritten baseline, allocating 688 B versus 560 B. Compare the intervals before interpreting a small timing difference.
- Yielding diamond: TedPipeline averages 1.40× handwritten, allocating 1,693 B versus 720 B. Parallel cancellation coordination and intermediate results still cost memory and time.
- Mixed completion-only execution averages 0.95× handwritten with 536 B versus 536 B.
- The yielding chain allocates about 33% fewer bytes than the WorkflowFramework typed-pipeline adapter (688 versus 1,032 B). The yielding diamond allocates about 62% fewer bytes than its parallel-workflow adapter (1,693 versus 4,438 B); its mean latency is also lower (5.73 versus 9.22 μs). These are measured adapter/workload advantages, not feature-equivalent guarantees.
- Relative to the other library adapters, the tables identify where lower latency or fewer allocations are observed. They do not establish a universal ranking. Workflow scheduling and buffering features excluded from this benchmark may be the reason to choose another library.
- Choose TedPipeline when the graph is static and typed wiring, generated orchestration, and shared execution policies save useful code. Choose handwritten code when minimizing orchestration overhead matters more than those conveniences. Durable or distributed workflows and streaming backpressure are outside this library's purpose.

## Reproduction and evidence

Run the commands in the [benchmark README](README.md). The source snapshot is frozen: [hash manifest](results/2026-09-05/readability/source.sha256), [source ZIP](results/2026-09-05/readability/source.zip), [full log](results/2026-09-05/readability/run.log), [test result](results/2026-09-05/readability/tests.log), and [adapter verification](results/2026-09-05/readability/adapter-verification.log).

Original BenchmarkDotNet Markdown, CSV and full JSON (individual measurements) are preserved alongside these files:

- [ChainBenchmarks github.md](results/2026-09-05/readability/TedToolkit.Orchestration.Pipeline.Benchmarks.ChainBenchmarks-report-github.md)
- [ChainBenchmarks full.json](results/2026-09-05/readability/TedToolkit.Orchestration.Pipeline.Benchmarks.ChainBenchmarks-report-full.json)
- [DiamondBenchmarks github.md](results/2026-09-05/readability/TedToolkit.Orchestration.Pipeline.Benchmarks.DiamondBenchmarks-report-github.md)
- [DiamondBenchmarks full.json](results/2026-09-05/readability/TedToolkit.Orchestration.Pipeline.Benchmarks.DiamondBenchmarks-report-full.json)
- [ValueSyncBenchmarks github.md](results/2026-09-05/readability/TedToolkit.Orchestration.Pipeline.Benchmarks.ValueSyncBenchmarks-report-github.md)
- [ValueSyncBenchmarks full.json](results/2026-09-05/readability/TedToolkit.Orchestration.Pipeline.Benchmarks.ValueSyncBenchmarks-report-full.json)
- [ValueAsyncBenchmarks github.md](results/2026-09-05/readability/TedToolkit.Orchestration.Pipeline.Benchmarks.ValueAsyncBenchmarks-report-github.md)
- [ValueAsyncBenchmarks full.json](results/2026-09-05/readability/TedToolkit.Orchestration.Pipeline.Benchmarks.ValueAsyncBenchmarks-report-full.json)
