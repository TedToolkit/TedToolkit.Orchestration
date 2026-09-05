# Completed Task result access — 2026-09-05

Decision: retain GetAwaiter().GetResult() only after a successful await has established that the relevant tasks have completed. Keep await ConfigureAwait(false) where dependency completion is still pending.

The isolated read benchmark favors GetResult. The larger async compositions do not establish a stable throughput advantage for replacing it with await: the snapshot winner reversed on repetition, and the repeated confidence intervals overlap. This is not evidence that blocking on an incomplete task is safe, nor a claim about whole-pipeline performance.

## Method and reproducibility

Source: [CompletedTaskBenchmarks.cs](CompletedTaskBenchmarks.cs), SHA-256 F7B248B6941433988FBE632B1337E19652F9148378BB325819E34F1A491AB805.

BenchmarkDotNet 0.15.8, .NET SDK 10.0.400, .NET runtime 10.0.11, x64 RyuJIT x86-64-v3, Windows 11 25H2 build 26200.9168, Intel Core i7-12700H (14 physical / 20 logical cores). Default workstation GC; no processor affinity was imposed. No other builds or tests were launched while samples were being collected.

Both candidates return Task<int>. Task creation and correctness checks occur in GlobalSetup. Both candidates produce identical integer results. The read comparison uses the same async prologue and 256 distinct completed Task<int> values per invocation; OperationsPerInvoke normalizes timing to one read. The join comparison includes generic Task.WhenAll followed by reading two results. The snapshot comparison uses non-generic Task.WhenAll, analogous to the wait inside ParallelExecution.CompleteAsync, followed by reading two results. It does not include the full ParallelExecution implementation or generated Results struct.

Initial run: two launches, eight warmups and twenty measured iterations per launch, 500 ms target per iteration. Six benchmark cases completed successfully.
Snapshot repetition: three launches, ten warmups and twenty-five measured iterations per launch, 500 ms target per iteration. Two benchmark cases completed successfully.

Run from the repository root:

~~~powershell
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --filter '*CompletedTask*Benchmarks*' --launchCount 2 --warmupCount 8 --iterationCount 20 --iterationTime 500 --artifacts artifacts/completed-task-benchmarks

dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*CompletedTaskSnapshotBenchmarks*' --launchCount 3 --warmupCount 10 --iterationCount 25 --iterationTime 500 --artifacts artifacts/completed-task-snapshot-repeat
~~~

## Results

Mean ± Error in nanoseconds; Error is half the 99.9% confidence interval reported by BenchmarkDotNet.

| Scenario | GetAwaiter().GetResult() | await ConfigureAwait(false) | Allocated, each candidate |
| --- | ---: | ---: | ---: |
| Isolated completed result read, per item | 0.7710 ± 0.0284 | 2.1153 ± 0.2605 | See normalization note |
| Two typed inputs after generic WhenAll, whole call | 95.14 ± 3.147 | 102.77 ± 6.351 | 232 B |
| Snapshot after non-generic WhenAll, initial run | 63.76 ± 5.966 | 47.03 ± 2.813 | 144 B |
| Snapshot, repeated run | 42.37 ± 1.641 | 45.01 ± 1.573 | 144 B |

The isolated difference is approximately 1.34 ns per read (await takes about 2.74 times the arithmetic mean). Do not apply that ratio to an entire pipeline.

Allocation normalization matters: the read case divides a complete async invocation by 256 reads. BenchmarkDotNet displays "-" for allocated bytes at this granularity. That does not prove a complete invocation allocates nothing; both candidates still return a Task<int>. No allocation advantage between the two candidates was observed. Join/snapshot allocations include their waiting machinery and returned task, not just result access.

## Uncertainty and interpretation

BenchmarkDotNet flagged bimodal distributions for the initial await read and await join cases, and a multimodal distribution for repeated snapshot GetResult. It removed five read GetResult outliers, four initial snapshot await outliers, five repeated snapshot GetResult outliers, and two repeated snapshot await outliers. These warnings and the change between runs limit interpretation of small aggregate differences.

The initial snapshot result favored await, but repeating that same source with more launches and iterations reversed the direction. The repeated snapshot intervals overlap, as do the initial join intervals. There is no reproducible aggregate advantage here that warrants replacing the existing access.

No generator behavior was changed. The precondition is explicit at both use sites: upstream task reads occur after successful WhenAll; Results reads occur after successful CompleteAsync. Pending dependencies continue to be awaited, and failures are handled before results are accessed.

Raw artifacts remain under the ignored artifacts directory:

- [Initial run log](../../artifacts/completed-task-benchmark-run.log)
- [Read report](../../artifacts/completed-task-benchmarks/results/TedToolkit.Orchestration.Pipeline.Benchmarks.CompletedTaskReadBenchmarks-report-github.md)
- [Join report](../../artifacts/completed-task-benchmarks/results/TedToolkit.Orchestration.Pipeline.Benchmarks.CompletedTaskJoinBenchmarks-report-github.md)
- [Initial snapshot report](../../artifacts/completed-task-benchmarks/results/TedToolkit.Orchestration.Pipeline.Benchmarks.CompletedTaskSnapshotBenchmarks-report-github.md)
- [Repeated run log](../../artifacts/completed-task-snapshot-repeat.log)
- [Repeated snapshot report](../../artifacts/completed-task-snapshot-repeat/results/TedToolkit.Orchestration.Pipeline.Benchmarks.CompletedTaskSnapshotBenchmarks-report-github.md)
