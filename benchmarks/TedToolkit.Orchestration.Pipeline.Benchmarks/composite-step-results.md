# Composite Step nesting benchmark

Recorded 2026-09-08 on Windows 11 25H2 with an Intel Core i7-12700H, .NET SDK 10.0.400, .NET 10.0.11 x64, and BenchmarkDotNet 0.15.8. Each job used two launches, five warmups, fifteen measured iterations, and a 250 ms target iteration time. Pipeline facade construction occurred in benchmark setup.

## Results

| Path | Shape | Mean | 99.9% confidence interval | Allocation |
| --- | --- | ---: | ---: | ---: |
| Synchronous | Flat | 5.235 ns | 4.664–5.806 ns | 0 B |
| Synchronous | Nested | 6.264 ns | 5.714–6.814 ns | 0 B |
| Completed task | Direct | 26.75 ns | 25.761–27.739 ns | 216 B |
| Completed task | Flat | 47.79 ns | 45.420–50.160 ns | 288 B |
| Completed task | Nested | 55.80 ns | 51.559–60.041 ns | 360 B |
| Yielding task | Direct | 1.537 μs | 1.476–1.598 μs | 320 B |
| Yielding task | Flat | 1.793 μs | 1.670–1.915 μs | 440 B |
| Yielding task | Nested | 1.863 μs | 1.718–2.008 μs | 560 B |

The synchronous and yielding flat/nested 99.9% confidence intervals overlap. The completed path adds 8.01 ns and 72 B, within its approved absolute limits of 16 ns and 512 B. The yielding nested path adds 120 B, and the synchronous path remains allocation-free. All revised AC-07 boundaries pass; the absolute completed-Task limit avoids magnifying a small fixed boundary cost against a sub-50 ns baseline.

BenchmarkDotNet reported a bimodal synchronous nested distribution and multimodality in the asynchronous run. This is why acceptance uses the reported confidence intervals and an absolute completed-task limit rather than treating a single mean ratio as conclusive.

The direct asynchronous rows are lower-abstraction context only. They do not include generated graph coordination and are not used for the flat-versus-nested acceptance decision.

## Maintained comparison rerun

The same job also completed all 24 existing Chain, Diamond, ValueSync, and ValueAsync cases. The current generated-path rows were:

| Workload | Mode | Mean | Allocation |
| --- | --- | ---: | ---: |
| Chain / TedPipeline | Completed | 60.32 ns | 440 B |
| Chain / TedPipeline | Yield | 2.895 μs | 680 B |
| Diamond / TedPipeline | Completed | 258.3 ns | 848 B |
| Diamond / TedPipeline | Yield | 4.362 μs | 1,682 B |
| ValueSync / Generated | Synchronous | 11.645 ns | 0 B |
| ValueAsync / Generated | Completed | 45.56 ns | 288 B |
| ValueAsync / Generated | Yield | 2.857 μs | 544 B |

These rows show that every maintained comparison remained executable after the breaking API migration. They are a new same-job snapshot, not a causal comparison with earlier sessions.

## Commands

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*CompositeSyncNestingBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-sync
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*CompositeNestingBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-async
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*ChainBenchmarks*' '*DiamondBenchmarks*' '*ValueSyncBenchmarks*' '*ValueAsyncBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-maintained
```

BenchmarkDotNet ran each case in a separate child process. No processor affinity was imposed. Treat these measurements as evidence for this machine and candidate, and do not infer a causal comparison with results from another session.
