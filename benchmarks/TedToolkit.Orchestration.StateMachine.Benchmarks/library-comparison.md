# StateMachine library comparison

Measured on 2026-09-05 with BenchmarkDotNet 0.15.8, .NET 10.0.11, Windows 11 25H2, and an Intel Core i7-12700H. Each case used two launches, five warmups, fifteen measured iterations, and a 250 ms target iteration time.

Compared implementations:

- TedToolkit.Orchestration.StateMachine from this workspace
- Stateless 5.20.1
- Appccelerate.StateMachine 6.0.0
- a direct enum-switch implementation as a lower-bound reference

Before measurement, the adapters verify equivalent state, guard, and lifecycle outcomes. Execution setup is outside the measured methods.

## Results

### One observable toggle

| Implementation | Mean | Allocated |
| --- | ---: | ---: |
| Handwritten | ~0.09 ns* | 0 B |
| TedToolkit | 30.40 ns | 0 B |
| Stateless | 300.60 ns | 1,208 B |
| Appccelerate | 282.60 ns | 1,544 B |

TedToolkit measured about 9.9x faster than Stateless and 9.3x faster than Appccelerate in this workload, with no per-trigger managed allocation. Its two public transition events had no subscribers in this hot-path case.

After callback-boundary reentry protection was added, a TedToolkit-only follow-up using the same launch, warmup, iteration, and iteration-time settings measured 32.06 ns and 0 B. The other adapters were not rerun in that follow-up, so the table and its ratios remain the internally consistent full-comparison run. The follow-up shows that the no-callback path remains allocation-free with little added cost.

### Guard plus exit and entry callbacks

| Implementation | Mean | Allocated |
| --- | ---: | ---: |
| Handwritten | ~0.19 ns* | 0 B |
| TedToolkit | 32.36 ns | 0 B |
| Stateless | 376.87 ns | 1,208 B |
| Appccelerate | 414.91 ns | 1,544 B |

TedToolkit measured about 11.6x faster than Stateless and 12.8x faster than Appccelerate while running an equivalent guard, exit callback, state commit, and entry callback. The comparison-library intervals were wide in this run, so the exact ratios should not be generalized.

With callback-boundary reentry protection and disposable callback scopes, a TedToolkit-only follow-up measured 55.87 ns and 0 B. This is higher than the 32.36 ns result above because this workload enters and disposes protection around its guard, exit callback, and entry callback. It confirms that the value-type scope does not allocate; the cost belongs to the enabled callback protection. The other adapters were not rerun, so no new cross-library ratio is claimed.

### Capability query

| Implementation | Mean | Allocated |
| --- | ---: | ---: |
| Handwritten | ~0.06 ns* | 0 B |
| TedToolkit | 4.87 ns | 0 B |
| Stateless | 143.24 ns | 616 B |

Appccelerate is omitted from this case because the selected adapter does not expose an equivalent direct capability-query operation. TedToolkit measured about 29x faster than Stateless here.

### Construction

| Implementation | Mean | Allocated |
| --- | ---: | ---: |
| Handwritten | 4.01 ns | 24 B |
| TedToolkit | 3.87 ns | 24 B |
| Stateless | 287.67 ns | 2,456 B |
| Appccelerate | 358.38 ns | 1,856 B |

The TedToolkit and handwritten confidence intervals overlap, so this run does not establish a difference between them. Construction is also not an apples-to-apples library ranking: TedToolkit generates routing at compile time, Stateless configures each measured instance, and Appccelerate reuses a definition before creating and starting an instance.

## Interpretation and limits

The result supports the intended architecture: generated direct dispatch keeps the common trigger path allocation-free and materially below these two runtime-configured libraries. It does not rank feature breadth, hierarchy, persistence, graph export, concurrency behavior, or workloads dominated by user callbacks.

*The handwritten execution and capability-query measurements are indistinguishable or close to BenchmarkDotNet's empty-method overhead. They are retained only as a directional lower bound; ratios against them are not meaningful.

Raw BenchmarkDotNet reports:

- [transition](results/2026-09-05/transition-events/results/TedToolkit.Orchestration.StateMachine.Benchmarks.TransitionBenchmarks-report-github.md)
- [transition after reentry protection](results/2026-09-05/reentrancy/results/TedToolkit.Orchestration.StateMachine.Benchmarks.TransitionBenchmarks-report-github.md)
- [guard and lifecycle](results/2026-09-05/transition-events/results/TedToolkit.Orchestration.StateMachine.Benchmarks.GuardAndLifecycleBenchmarks-report-github.md)
- [guard and lifecycle with disposable callback scopes](results/2026-09-05/reentrancy/results/TedToolkit.Orchestration.StateMachine.Benchmarks.GuardAndLifecycleBenchmarks-report-github.md)
- [capability query](results/2026-09-05/observable-toggle/results/TedToolkit.Orchestration.StateMachine.Benchmarks.CanFireBenchmarks-report-github.md)
- [construction](results/2026-09-05/observable-toggle/results/TedToolkit.Orchestration.StateMachine.Benchmarks.ConstructionBenchmarks-report-github.md)

## Reproduce

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks --configuration Release --no-build -- --filter '*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250
```

Package references: [Stateless 5.20.1](https://www.nuget.org/packages/Stateless/5.20.1) and [Appccelerate.StateMachine 6.0.0](https://www.nuget.org/packages/Appccelerate.StateMachine/6.0.0).
