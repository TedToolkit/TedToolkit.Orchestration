# Pipeline library comparison before cancellation/task-reuse optimization — 2026-09-05

This measures the current direct-Step task composition, not the older branch-based generator. All 24 benchmark cases completed successfully. The full regression suite passed 116 tests; the adapter verification passed all 18 library/graph/mode combinations and three synchronous/mixed checks before timing.

## CompleteAsync decision

Retain CompleteAsync. It already awaits Task.WhenAll, then restores caller-cancellation precedence and propagates the first observed terminal exception. Bare WhenAll can select a different exception according to the task input ordering and does not restore the caller's original token. CompletionReportsTheFirstObservedFailureNotTheFirstTaskInArgumentOrder demonstrates that difference with two failures arriving in the opposite order to their task arguments. Existing cancellation/draining tests verify that started work finishes cleanup before the pipeline returns.

No runtime or generator behavior was changed in this comparison. New tests cover synchronous → asynchronous → synchronous execution, synchronous disposal, both result modes, mixed synchronous/asynchronous diamond readiness, and first-observed failure propagation.

## Workloads and limits

- Chain: four additions; input 1024 produces 1028.
- Diamond: one source, two independent operations, then a sum; input 1024 produces 2053. The shared source runs once.
- Completed: business Steps use asynchronous interfaces but return already completed Task<int> values. This is not a pure-synchronous Pipeline.
- Yield: each asynchronous payload uses Task.Yield, measuring real suspension and scheduling.
- Pure synchronous: four synchronous additions and one synchronous result sink, comparing void handwritten and generated methods; output is 1028.
- Mixed completion-only: four asynchronous additions followed by a synchronous sink, without a Results snapshot; output is 1028. This differs from the four-operation result-returning chain and should not be compared to that group's absolute times.

Values stay outside the small Task<int> result cache. Runners are constructed before timing. No source generation, construction, DI setup, retries, finite timeouts, failures or logging are measured. Dataflow has one message in flight, so these are request-latency results, not sustained stream throughput.

The adapters execute the same successful payload, but do not have identical framework features. DirectTasks is a minimal handwritten lower bound: it does not implement TedPipeline's full cancellation/failure handling or intermediate Results snapshot. WorkflowFramework uses its native typed pipeline for chains and typed context/parallel workflow for diamonds. PipelineNet uses its standard resolver, which creates middleware per invocation. Dataflow uses its native scheduling and buffering. PipelineNet is not presented as a diamond implementation.

These successful-path measurements cannot isolate CompleteAsync's cost or prove that removing it is safe. The nanosecond completed-task-access study is separate.

## Environment and method

BenchmarkDotNet 0.15.8; .NET SDK 10.0.400; .NET runtime 10.0.11, x64 RyuJIT x86-64-v3; Windows 11 25H2 build 26200.9168; Intel Core i7-12700H, 14 physical / 20 logical cores. Workstation concurrent GC. No processor affinity imposed.

Versions: PipelineNet 0.11.0, WorkflowFramework 1.0.10, TPL Dataflow from the .NET 10 runtime. These are the repository's pinned versions, not a survey of every available library/version.

One launch, five warmups, ten measurement iterations, 250 ms target per iteration. Total BenchmarkDotNet time: 174.63 seconds. This is a local single-launch comparison; Error is half the 99.9% confidence interval. Small differences with overlapping intervals are inconclusive. Dataflow diamond Yield had relatively high variance. BenchmarkDotNet reported outlier removal for WorkflowFramework groups; consult the full logs for retained sample counts. No additional builds/tests ran while timing samples.

The immutable timing input is recorded in [the 34-file source hash manifest](../../artifacts/library-comparison-source.sha256). It covers production code, benchmark code, project files and dependency/build properties.

## Findings

- Chain/Yield: TedPipeline averages 3.558 μs, 1.35× the minimal handwritten 2.643 μs. WorkflowFramework's typed chain is faster at 3.009 μs; PipelineNet and Dataflow are slower.
- Diamond/Yield: TedPipeline averages 3.263 μs, 1.41× handwritten 2.314 μs. It is faster than WorkflowFramework's parallel workflow and Dataflow in this workload.
- Mixed/Yield completion-only: TedPipeline averages 2.991 μs versus handwritten 2.605 μs, about 15% overhead. Allocation is 1003 B versus 536 B.
- Pure synchronous: 12.811 ns generated versus 2.510 ns handwritten, about 10.3 ns absolute overhead / 5.11× ratio. Both report zero allocated bytes, consistent with the existing allocation regression.
- Async allocation remains higher than minimal handwritten code: 1158 B versus 560 B for Chain/Yield and 2119 B versus 720 B for Diamond/Yield. Generated Step task methods, invocation coordination and Results collection are present in the implementation; this benchmark does not separately attribute bytes or time to each component.
- Completed-task chain means for TedPipeline and WorkflowFramework are close and their error intervals overlap. Do not claim either wins that pair.

## Detailed measurements

Tables are copied directly from BenchmarkDotNet reports. Units are nanoseconds per complete execution, except where a report explicitly states otherwise. "-" in the synchronous allocation column means no measured managed allocation.

### Chain

| Method                | Mode      | Mean        | Error      | StdDev     | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |---------- |------------:|-----------:|-----------:|-------:|--------:|-------:|----------:|------------:|
| **DirectTasks**           | **Completed** |    **28.86 ns** |   **1.076 ns** |   **0.640 ns** |   **1.00** |    **0.03** | **0.0286** |     **360 B** |        **1.00** |
| TedPipeline           | Completed |    87.14 ns |   6.009 ns |   3.576 ns |   3.02 |    0.13 | 0.0578 |     728 B |        2.02 |
| WorkflowTypedPipeline | Completed |    85.08 ns |   8.978 ns |   5.938 ns |   2.95 |    0.21 | 0.0516 |     648 B |        1.80 |
| PipelineNet           | Completed |   335.76 ns |  17.726 ns |  11.724 ns |  11.64 |    0.46 | 0.0639 |     808 B |        2.24 |
| TplDataflow           | Completed | 8,020.91 ns | 281.795 ns | 167.692 ns | 278.07 |    8.03 | 0.1575 |    2032 B |        5.64 |
|                       |           |             |            |            |        |         |        |           |             |
| **DirectTasks**           | **Yield**     | **2,642.92 ns** |  **87.503 ns** |  **57.878 ns** |   **1.00** |    **0.03** | **0.0426** |     **560 B** |        **1.00** |
| TedPipeline           | Yield     | 3,557.59 ns | 110.757 ns |  73.259 ns |   1.35 |    0.04 | 0.0814 |    1158 B |        2.07 |
| WorkflowTypedPipeline | Yield     | 3,009.25 ns | 118.070 ns |  61.753 ns |   1.14 |    0.03 | 0.0765 |    1031 B |        1.84 |
| PipelineNet           | Yield     | 4,919.60 ns | 216.338 ns | 143.094 ns |   1.86 |    0.07 | 0.2078 |    2837 B |        5.07 |
| TplDataflow           | Yield     | 9,556.04 ns | 424.045 ns | 252.342 ns |   3.62 |    0.12 | 0.1555 |    2161 B |        3.86 |

### Diamond

| Method           | Mode      | Mean         | Error        | StdDev       | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------- |-------------:|-------------:|-------------:|------:|--------:|-------:|----------:|------------:|
| **DirectTasks**      | **Completed** |     **86.10 ns** |     **9.230 ns** |     **6.105 ns** |  **1.00** |    **0.09** | **0.0411** |     **520 B** |        **1.00** |
| TedPipeline      | Completed |    209.85 ns |    11.493 ns |     7.602 ns |  2.45 |    0.18 | 0.0880 |    1104 B |        2.12 |
| WorkflowParallel | Completed |    724.39 ns |    15.805 ns |    10.454 ns |  8.45 |    0.56 | 0.1900 |    2408 B |        4.63 |
| TplDataflow      | Completed |  7,555.32 ns |   156.353 ns |   103.418 ns | 88.14 |    5.82 | 0.1645 |    2188 B |        4.21 |
|                  |           |              |              |              |       |         |        |           |             |
| **DirectTasks**      | **Yield**     |  **2,314.12 ns** |    **24.226 ns** |    **16.024 ns** |  **1.00** |    **0.01** | **0.0548** |     **720 B** |        **1.00** |
| TedPipeline      | Yield     |  3,262.74 ns |    32.662 ns |    21.604 ns |  1.41 |    0.01 | 0.1655 |    2119 B |        2.94 |
| WorkflowParallel | Yield     |  5,041.52 ns |    73.727 ns |    38.560 ns |  2.18 |    0.02 | 0.3549 |    4428 B |        6.15 |
| TplDataflow      | Yield     | 15,148.04 ns | 2,661.706 ns | 1,760.554 ns |  6.55 |    0.73 | 0.1096 |    2347 B |        3.26 |

### Mixed completion-only

| Method      | Mode      | Mean        | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------ |---------- |------------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **Handwritten** | **Completed** |    **25.56 ns** |   **1.794 ns** |  **1.187 ns** |  **1.00** |    **0.06** | **0.0229** |     **288 B** |        **1.00** |
| Generated   | Completed |    70.54 ns |   3.954 ns |  2.615 ns |  2.76 |    0.15 | 0.0458 |     576 B |        2.00 |
|             |           |             |            |           |       |         |        |           |             |
| **Handwritten** | **Yield**     | **2,604.55 ns** | **141.924 ns** | **93.874 ns** |  **1.00** |    **0.05** | **0.0334** |     **536 B** |        **1.00** |
| Generated   | Yield     | 2,990.52 ns |  65.505 ns | 38.981 ns |  1.15 |    0.04 | 0.0735 |    1003 B |        1.87 |

### Pure synchronous completion-only

| Method      | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Handwritten |  2.510 ns | 0.0956 ns | 0.0633 ns |  1.00 |    0.03 |         - |          NA |
| Generated   | 12.811 ns | 0.6361 ns | 0.4207 ns |  5.11 |    0.20 |         - |          NA |

## Reproduce

Run from the repository root:

~~~powershell
dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests --configuration Release

dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify

dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*ChainBenchmarks*' '*DiamondBenchmarks*' '*ValueSyncBenchmarks*' '*ValueAsyncBenchmarks*' --launchCount 1 --warmupCount 5 --iterationCount 10 --iterationTime 250 --artifacts artifacts/library-comparison-20260905
~~~

Evidence remains in ignored build artifacts:

- [Regression log](../../artifacts/mixed-execution-tests.log)
- [Adapter correctness log](../../artifacts/library-comparison-verify.log)
- [Full benchmark log](../../artifacts/library-comparison-run.log)
- [Raw reports, JSON and CSV](../../artifacts/library-comparison-20260905/results)

Source: [mixed execution tests](../../tests/TedToolkit.Orchestration.Pipeline.Tests/MixedExecutionTests.cs), [market comparison definitions](ExecutionBenchmarks.cs), [synchronous/mixed comparison definitions](ValueExecutionBenchmarks.cs).
