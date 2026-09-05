# Historical benchmark notes

This preserves earlier documentation. References to the current API below describe those historical runs. Use the [benchmark entry point](README.md) for the latest comparison.

# Pipeline benchmarks

Latest: [cancellation and Task-reuse optimization, with matched handwritten baselines](cancellation-optimization.md).

Before optimization: [2026-09-05 comparison of the current generator, handwritten code, WorkflowFramework, PipelineNet and TPL Dataflow](current-library-comparison.md), including synchronous and mixed execution. Older results below describe historical implementations.



Reproducible, in-process comparisons for [TedToolkit.Orchestration.Pipeline](../../README.md). This project is deliberately outside the main solution, normal CI, and NuGet packaging; its external libraries are benchmark-only dependencies.



## Run



From the repository root, with the .NET 10 SDK:



```shell

dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify

dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*'

```



The first command checks every implementation with inputs 1024, 2048, and 1024 again, in both completion modes. The second runs BenchmarkDotNet in separate processes. Each benchmark returns the final integer; no execution is fire-and-forget. Generated results go to the ignored `artifacts/benchmarks` directory.



Use `--filter '*ChainBenchmarks*'`, `'*DiamondBenchmarks*'`, or `'*ConstructionBenchmarks*'` to select a group. Extend sampling with `--iterationTime 1000`. Keep other CPU-heavy work stopped during measurements.



## Named executors: synchronous confirmation, 2026-09-04

The earlier named-executor API was measured with the same four additions and result sink, excluding construction and DI and using `ExecuteWithoutResultsAsync`. Both paths store 1028. One launch, five warmups, ten measurement iterations, 500 ms target; same .NET 10 workstation described below.

| Implementation | Mean ± error | Allocated / operation |
| --- | ---: | ---: |
| Handwritten | 2.637 ± 0.196 ns | 0 B |
| Generated named executor | 4.902 ± 0.448 ns | 0 B |

Error is half the 99.9% confidence interval. The generated sample is about 1.86 times the handwritten mean; no claim of equal speed is made. BenchmarkDotNet removed one generated outlier and reported an MValue of 3.2. These tiny timings are local observations and do not establish a percentage improvement over earlier runs under different conditions. Zero allocated bytes is also checked separately by a regression test. Async, result-collecting, construction and market-comparison timing groups have not been remeasured for this API; their adapters compile and the correctness verification passes.

[Measurements, log, command, source hashes and source ZIP](results/2026-09-04/named-executors-sync/) preserve this exact candidate. Historical reports below remain unchanged.

## Historical value-step implementation: 2026-09-04

The recorded API uses ref struct steps and struct builders. The current named-executor API infers execution parameters, captures configuration in fields, and returns typed result structs; the retained measurements predate that migration. A separate handwritten comparison measures four additions followed by a sink, using `BuildWithoutResults` so neither implementation collects a result snapshot. Both implementations execute the same payload and store the final value; setup verifies 1028. Construction, DI, retries and finite timeouts are excluded.

| Execution mode | Handwritten mean ± error | Generated mean ± error | Handwritten / generated allocation |
| --- | ---: | ---: | ---: |
| Synchronous contracts | 4.348 ± 0.291 ns | 12.086 ± 0.809 ns | 0 B / 0 B |
| Async contracts, completed tasks | 54.88 ± 1.528 ns | 66.72 ± 3.858 ns | 288 B / 288 B |
| Async contracts, Task.Yield | 3919.46 ± 240.867 ns | 3797.26 ± 136.515 ns | 536 B / 544 B |

These are the [final source snapshot's results](results/2026-09-04/value-steps-final/), with 500 ms iterations, one launch, five warmups and ten measurements. Error is half the 99.9% confidence interval. **A background compiler remained active during this run**, so latency and ratios are exploratory observations under load; see the [environment notes](results/2026-09-04/value-steps-final/environment-notes.md). The yielding intervals overlap. The synchronous generated path is still slower than handwritten in this sample (ratio 2.78). Zero bytes applies to this warmed-up synchronous completion-only workload, and is also checked by an allocation regression test; it is not a blanket pipeline guarantee.

The [initial value-step run](results/2026-09-04/value-steps-initial/) measured six cases. Its synchronous generated path took 21.700 ns before removing the outer async success state machine. A [synchronous confirmation](results/2026-09-04/value-steps-sync-confirmation/) then measured 13.506 ns versus 3.709 ns handwritten, both 0 B. The final snapshot additionally removes a redundant per-step cancellation check and remeasures all six cases. Different run conditions prevent attributing the final timing difference solely to that change. Every folder retains raw measurements, logs, the exact command, source hashes and a source ZIP. Historical reports below belong to the earlier class-based implementation and must not be used to rank the current version against other libraries.

Reproduce these groups with `--filter '*ValueSyncBenchmarks*'` and `--filter '*ValueAsyncBenchmarks*'`. These completion-only benchmarks have a different result contract from the historical market comparison; do not compare their allocation columns directly.

## What is compared



| Implementation | Version | API and scope |

| --- | --- | --- |

| Ted Pipeline | Local 1.0.0 sources | Generated builders/executors, default policies, typed result retrieval. |

| [WorkflowFramework](https://github.com/JerrettDavis/WorkflowFramework/tree/2e5fdb7c36af1b1fa1e352ba2db405f50be5702e) | 1.0.10 | Its lightweight typed pipeline for chains; its typed workflow and native parallel steps for diamonds. |

| [PipelineNet](https://github.com/ipvalverde/PipelineNet) | 0.11.0 | AsyncPipeline with its standard ActivatorMiddlewareResolver; chain only. |

| [TPL Dataflow](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/dataflow-task-parallel-library) | .NET 10.0.11 runtime | Reused TransformBlocks; broadcast and join blocks for the diamond. |

| Direct Tasks | .NET 10.0.11 runtime | Handwritten await/WhenAll baseline, without framework lifecycle or policy features. |



WorkflowFramework and PipelineNet cover reusable in-process workflow/pipeline APIs. Dataflow is a related data-processing primitive with queueing and streaming features. These are **equivalent payloads, not identical feature sets**. PipelineNet is not forced into a DAG it does not natively represent; persistence, distributed workflow engines, web hosts, and dashboards are outside this comparison.



## Workloads and fairness



- **Chain:** four dependent additions, `input -> +1 -> +1 -> +1 -> +1`. Input 1024 must produce 1028.

- **Diamond:** one source `input + 1`, two independent branches `source + 1` / `source + 2`, then a sum. Input 1024 must produce 2053. Four payload calls; the shared source is represented once.

- **Completed:** each payload returns `Task.FromResult`. This isolates overhead for tiny operations, not application throughput.

- **Yield:** each payload first executes `Task.Yield()`. This measures asynchronous scheduling/continuation overhead; it is not a network, disk, or timed-I/O simulation.

- **Construction:** allocate and wire one reusable four-step chain. Includes Ted's executor construction and fixed-value capture, but excludes compiler/source-generation time, JIT, and service-provider creation. Dataflow construction is omitted because its asynchronous completion lifecycle needs a separate measurement contract.



All adapters call the same addition implementation in [Work.cs](Work.cs); results are outside the small-integer Task cache. Definition construction is excluded from execution measurements via GlobalSetup. One execution/message is in flight at a time. Dataflow blocks remain alive between messages, so these numbers describe single-message latency, not its potential batched streaming throughput. Completion and disposal happen during GlobalCleanup.



The historical market-comparison runs include default retry/timeout checks, per-node tasks, linked cancellation, class instances, and result snapshots. The current generator uses ref struct steps and direct calls for linear chains; parallel graphs coordinate typed tasks per Step without branch methods. The historical measurements do not measure this task composition. There are no injected benchmark services, retry attempts, finite timeouts, failures, logging, or disposable payload objects. PipelineNet uses fresh default-resolved middleware. WorkflowFramework's typed pipeline reuses delegates; its parallel workflow receives a fresh typed context on every invocation, retaining its normal context/result bookkeeping. These choices use each API normally rather than adding artificial wrappers to equalize functionality.



MemoryDiagnoser reports managed bytes allocated per operation, including framework and payload work; it is not peak memory or retained memory. Setup allocations are excluded from execution columns.



## Historical class-based market results — 2026-09-04



Windows 11 25H2, Intel Core i7-12700H (14 physical / 20 logical cores), .NET SDK 10.0.400, .NET runtime 10.0.11, x64 RyuJIT, concurrent workstation GC, BenchmarkDotNet 0.15.8. Release build without an attached debugger. This is one local workstation, not a dedicated performance lab.



The initial run completed all 21 cases using one launch, five warmup iterations, ten measurement iterations, and a target of 250 ms per iteration. BenchmarkDotNet's default outlier policy was retained. Error is half the 99.9% confidence interval; small differences with overlapping intervals are inconclusive.



### Four-step chain — initial run



| Implementation | Mode | Mean ± error (µs) | Allocated / operation |

| --- | --- | ---: | ---: |

| DirectTasks | Completed | 0.029 ± 0.003 | 360 B |

| TedPipeline | Completed | 0.531 ± 0.013 | 1768 B |

| WorkflowTypedPipeline | Completed | 0.080 ± 0.007 | 648 B |

| PipelineNet | Completed | 0.353 ± 0.022 | 808 B |

| TplDataflow | Completed | 7.348 ± 0.488 | 2032 B |

| DirectTasks | Yield | 2.481 ± 0.074 | 560 B |

| TedPipeline | Yield | 5.301 ± 0.500 | 3967 B |

| WorkflowTypedPipeline | Yield | 2.566 ± 0.097 | 1031 B |

| PipelineNet | Yield | 4.638 ± 0.259 | 2847 B |

| TplDataflow | Yield | 9.708 ± 1.175 | 2162 B |



### Four-step diamond — longer confirmation run



The initial diamond run reported one iteration below BenchmarkDotNet's recommended 100 ms minimum. The **entire eight-case diamond group** was rerun with a 1,000 ms target; launch/warmup/measurement counts stayed unchanged. The confirmation completed without warnings. This table uses the confirmation, while both runs remain available for inspection.



| Implementation | Mode | Mean ± error (µs) | Allocated / operation |

| --- | --- | ---: | ---: |

| DirectTasks | Completed | 0.105 ± 0.013 | 520 B |

| TedPipeline | Completed | 0.625 ± 0.036 | 1928 B |

| WorkflowParallel | Completed | 0.841 ± 0.047 | 2408 B |

| TplDataflow | Completed | 9.076 ± 0.381 | 2184 B |

| DirectTasks | Yield | 2.591 ± 0.058 | 720 B |

| TedPipeline | Yield | 5.816 ± 0.571 | 4110 B |

| WorkflowParallel | Yield | 7.055 ± 1.066 | 4441 B |

| TplDataflow | Yield | 10.840 ± 1.301 | 2313 B |



### Four-step chain construction — initial run



| Implementation | Mean ± error (ns) | Allocated / operation |

| --- | ---: | ---: |

| TedPipeline | 114.491 ± 15.243 | 776 B |

| WorkflowTypedPipeline | 137.937 ± 18.267 | 808 B |

| PipelineNet | 65.367 ± 5.966 | 176 B |



### What the numbers support



- For a tiny, synchronously completed chain, Ted takes about **0.531 µs / 1,768 B**, versus **0.080 µs / 648 B** for WorkflowFramework's typed pipeline and **0.353 µs / 808 B** for PipelineNet. Generated code does not make Ted the lowest-overhead choice for this workload.

- In the completed diamond confirmation, Ted takes **0.625 µs / 1,928 B**, versus **0.841 µs / 2,408 B** for WorkflowFramework's parallel workflow. That is a lower measured time and allocation for this particular graph and API pairing.

- The yielding diamond means were **5.816 µs** for Ted and **7.055 µs** for WorkflowFramework, but their confidence intervals overlap. The initial run had the opposite ordering of means. **Do not claim an asynchronous winner from these runs.**

- Dataflow has higher single-message latency in this suite, but allocates less than Ted in the yielding cases. Its streaming throughput and backpressure capabilities were not measured.

- Construction cost is small in absolute terms and excluded from steady-state execution. Ted and WorkflowFramework's construction intervals overlap; a small difference in their means is not an established advantage.



An optimization follow-up should start with Ted's per-invocation allocations and tiny-chain overhead, using this unchanged suite as the baseline. These measurements do not identify allocation call stacks or prove which internal component dominates; that requires profiling.





## Evidence and limitations



These measurements predate the change from Task-backed result snapshots to direct value storage, the removal of retained builder graphs, the subsequent fusion of linear execution chains, and the ref struct contract migration. They describe the recorded source snapshots, not the current implementation; direct storage boxes value-type results.



The [initial results](results/2026-09-04/initial/) and [diamond confirmation](results/2026-09-04/diamond-confirmation/) retain generated Markdown, CSV, full JSON measurements, and the complete run log. [Source hashes](results/2026-09-04/initial/source-sha256.json) identify the source snapshot because the repository has no committed revision for this candidate; [resolved packages](results/2026-09-04/initial/resolved-packages.json) record dependencies.



The initial log includes a duplicate Markdown-exporter warning; the redundant registration was subsequently removed without changing any payload or adapter. Any follow-up measurements and their settings are recorded above, rather than replacing inconvenient results silently.



This suite does not establish large-graph scaling, multi-request throughput, real-I/O performance, DI-resolution cost, cancellation/failure latency, policy retries, compile time, Native AOT support, or performance on another OS/CPU. The adapters exercise common operations; the runtime test suite owns lifecycle and diagnostic correctness. Rerun with your actual workload before drawing broader conclusions.



Current synchronous benchmark code uses void ExecuteWithoutResults and an equivalent void handwritten baseline. The recorded measurements above predate direct Step construction and synchronous entry points; they are not measurements of this revision.


## Completed Task result access, 2026-09-05

The [completed Task comparison](completed-task-results.md) measures GetAwaiter().GetResult() against await ConfigureAwait(false), including a repeat of the noisy snapshot case. Isolated reads favor GetResult; aggregate differences are not stable enough to justify changing the generator. Both access sites retain the successful-wait precondition.
