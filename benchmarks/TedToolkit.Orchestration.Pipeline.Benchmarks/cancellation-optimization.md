# Cancellation and Task reuse optimization — 2026-09-05

The final implementation removes ParallelExecution and CompleteAsync while retaining cooperative peer cancellation and draining. All 120 regression tests and every benchmark adapter correctness check pass.

## Changes and contract

1. Execute owns CancellationTokenSource.CreateLinkedTokenSource and directly awaits the non-generic Task.WhenAll for all started Step tasks. No first-failure state object or completion wrapper remains.
2. A terminal Step failure calls the shared execution source's CancelAsync under exception isolation, which signals cancellation and awaits cancellation callbacks without allowing callback exceptions to mask the Step failure. Steps still await operation cleanup. CancellationToken itself cannot signal cancellation; its source is required.
3. Concurrent exception selection is delegated to Task.WhenAll. There is no extra first-observed ordering guarantee or invocation-wide rule overriding all faults with later caller cancellation. Tests require preservation of an original exception and completion of both failing Steps rather than relying on undocumented ordering between simultaneous faults. Within Step observation, caller cancellation retains its original token.
4. Policy-free serial async Step methods reuse the actual business Task. ObserveStepAsync returns it directly for an uncancelable token, or for completed success after cancellation validation. Pending cancelable operations use a shared observer; retry/timeout paths remain unchanged. A regression verifies Task reference identity, and both void/result tests verify cancellation still waits for operation completion.
5. Multi-input Step methods await their already-started upstream tasks directly. They do not allocate another WhenAll just to access the inputs; the outer wait owns draining. This does not serialize upstream execution.
6. The outer wait explicitly selects the non-generic overload, avoiding an unused typed result array when every task happens to return the same type.
7. Removed redundant per-Step cancellation checks in the serial entry; the Step methods retain their own pre/post execution checks.

No RunBranch or nested methods were reintroduced. All-synchronous pipelines remain synchronous and allocation-free on the tested successful completion-only path.

## Measurements

See the [pre-optimization comparison](current-library-comparison.md) for the earlier implementation and competitors. The same handwritten adapters were remeasured alongside this implementation. Payloads, inputs, result contracts and package versions were unchanged.

Environment: BenchmarkDotNet 0.15.8, .NET SDK 10.0.400, .NET 10.0.11 x64 RyuJIT, Windows 11 25H2, Intel i7-12700H (14 physical / 20 logical cores), workstation concurrent GC. No CPU affinity. Two launches, five warmups, fifteen measured iterations per launch, 250 ms target per iteration.

Phase A measured fourteen cases: handwritten/Ted chain and diamond with Completed/Yield modes, plus pure synchronous and mixed completion-only. Phase B changed only the outer parallel WhenAll overload and reran all four diamond cases. Phase A's nonparallel measurements remain applicable; its diamond measurements are retained as intermediate evidence, not substituted for final results. Total BenchmarkDotNet times were 250.66 s and 75.25 s respectively.

Source snapshots:
- [Phase A hash manifest](../../artifacts/cancellation-optimization-source.sha256)
- [Final hash manifest](../../artifacts/cancellation-optimization-final-source.sha256)

### Timing summary

Mean timings, with matched handwritten baselines from the same run:

| Scenario | Handwritten | Optimized Ted | Interpretation |
| --- | ---: | ---: | --- |
| Chain, completed async Tasks | 35.83 ns | 53.28 ns | ~1.49× mean; some async result/dispatch overhead remains |
| Chain, real Yield suspension | 2.531 μs | 2.513 μs | Error intervals overlap; effectively comparable in this run, not a proven speedup over handwritten |
| Diamond, completed async Tasks | 84.01 ns | 162.21 ns | ~1.93× mean |
| Diamond, real Yield suspension | 2.385 μs | 3.253 μs | ~36% above handwritten |
| Mixed completion-only, completed async Tasks | 53.61 ns | 67.70 ns | ~26% above handwritten |
| Mixed completion-only, real Yield suspension | 3.114 μs | 3.403 μs | ~9% higher mean; intervals overlap |
| Pure synchronous completion-only | 2.661 ns | 11.154 ns | ~8.5 ns absolute overhead remains |

The earlier handwritten baselines moved substantially between runs, especially for tiny completed operations. Do not subtract timings from different sessions and claim a precise percentage speedup. Several groups show multimodal distributions and BenchmarkDotNet removed outliers; detailed reports below include Error and StdDev. The small synchronous timing change does not establish a reliable improvement.

### Allocation comparison

Bytes per complete invocation, earlier Ted versus final Ted and the matched handwritten baseline:

| Scenario | Earlier Ted | Final Ted | Handwritten |
| --- | ---: | ---: | ---: |
| Chain, completed | 728 | 440 | 360 |
| Chain, Yield | 1158 | 688 | 560 |
| Diamond, completed | 1104 | 848 | 520 |
| Diamond, Yield | 2119 | 1734 | 720 |
| Mixed completion-only, completed | 576 | 288 | 288 |
| Mixed completion-only, Yield | 1003 | 536 | 536 |
| Pure synchronous completion-only | 0 | 0 | 0 |

The allocation reductions are stronger evidence than small timing differences. Mixed completion-only now matches handwritten allocation; typed result-returning chains and parallel graphs still pay for result collection and coordination. The handwritten market adapter is a minimal successful-path lower bound and does not implement all framework cancellation/result-snapshot behavior.

The source still contains separate synchronous Step calls and necessary exception handling. No claim is made that the remaining synchronous gap has been eliminated, and failure/cancellation semantics were not removed merely to make that microbenchmark inline better.

## Detailed final reports

Error is half the 99.9% confidence interval. "-" in allocation columns means no measured managed allocation for these unbatched invocation benchmarks.

### Chain (phase A, unchanged by phase B)

| Method      | Mode      | Mean        | Error     | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------ |---------- |------------:|----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **DirectTasks** | **Completed** |    **35.83 ns** |  **3.125 ns** |   **4.677 ns** |  **1.02** |    **0.18** | **0.0287** |     **360 B** |        **1.00** |
| TedPipeline | Completed |    53.28 ns |  5.775 ns |   8.282 ns |  1.51 |    0.29 | 0.0351 |     440 B |        1.22 |
|             |           |             |           |            |       |         |        |           |             |
| **DirectTasks** | **Yield**     | **2,530.79 ns** | **73.637 ns** | **107.936 ns** |  **1.00** |    **0.06** | **0.0406** |     **560 B** |        **1.00** |
| TedPipeline | Yield     | 2,512.97 ns | 84.102 ns | 123.276 ns |  0.99 |    0.06 | 0.0524 |     688 B |        1.23 |

### Diamond (final phase B)

| Method      | Mode      | Mean        | Error      | StdDev     | Median      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------ |---------- |------------:|-----------:|-----------:|------------:|------:|--------:|-------:|----------:|------------:|
| **DirectTasks** | **Completed** |    **84.01 ns** |   **3.576 ns** |   **5.242 ns** |    **84.47 ns** |  **1.00** |    **0.09** | **0.0414** |     **520 B** |        **1.00** |
| TedPipeline | Completed |   162.21 ns |   4.261 ns |   6.245 ns |   161.26 ns |  1.94 |    0.14 | 0.0675 |     848 B |        1.63 |
|             |           |             |            |            |             |       |         |        |           |             |
| **DirectTasks** | **Yield**     | **2,385.42 ns** |  **72.468 ns** | **106.223 ns** | **2,312.83 ns** |  **1.00** |    **0.06** | **0.0552** |     **720 B** |        **1.00** |
| TedPipeline | Yield     | 3,253.10 ns | 137.940 ns | 193.372 ns | 3,149.22 ns |  1.37 |    0.10 | 0.1330 |    1734 B |        2.41 |

### Mixed completion-only (phase A)

| Method      | Mode      | Mean        | Error      | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------ |---------- |------------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **Handwritten** | **Completed** |    **53.61 ns** |   **2.739 ns** |   **4.100 ns** |  **1.01** |    **0.11** | **0.0228** |     **288 B** |        **1.00** |
| Generated   | Completed |    67.70 ns |   4.392 ns |   6.574 ns |  1.27 |    0.16 | 0.0228 |     288 B |        1.00 |
|             |           |             |            |            |       |         |        |           |             |
| **Handwritten** | **Yield**     | **3,114.04 ns** | **144.706 ns** | **216.589 ns** |  **1.00** |    **0.10** | **0.0375** |     **536 B** |        **1.00** |
| Generated   | Yield     | 3,402.54 ns | 193.683 ns | 283.898 ns |  1.10 |    0.12 | 0.0293 |     536 B |        1.00 |

### Pure synchronous (phase A)

| Method      | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------ |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Handwritten |  2.661 ns | 0.4698 ns | 0.7031 ns |  1.08 |    0.45 |         - |          NA |
| Generated   | 11.154 ns | 0.9029 ns | 1.3234 ns |  4.54 |    1.54 |         - |          NA |

## Reproduce the final candidate

~~~powershell
dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests --configuration Release
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*ChainBenchmarks.DirectTasks*' '*ChainBenchmarks.TedPipeline*' '*DiamondBenchmarks.DirectTasks*' '*DiamondBenchmarks.TedPipeline*' '*ValueSyncBenchmarks*' '*ValueAsyncBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/optimization-reproduction
~~~

Evidence:
- [Regression log](../../artifacts/cancellation-optimization-tests.log)
- [Adapter verification](../../artifacts/cancellation-optimization-verify.log)
- [Phase A full log](../../artifacts/cancellation-optimization-benchmarks.log)
- [Final parallel full log](../../artifacts/cancellation-optimization-final-parallel.log)
- [Task reuse and cancellation tests](../../tests/TedToolkit.Orchestration.Pipeline.Tests/TaskReuseTests.cs)
