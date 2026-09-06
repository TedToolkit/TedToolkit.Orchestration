# StateMachine benchmarks

This project measures the generated hot paths of [TedToolkit.Orchestration.StateMachine](../../README.md) against a handwritten enum switch and two pinned, runtime-configured state-machine libraries. It checks whether compile-time routing produces the intended low-overhead execution while keeping comparison limits explicit.

The latest interpreted snapshot is the [2026-09-05 library comparison](library-comparison.md).

## What is compared

| Adapter | Version/source | Why it is present |
| --- | --- | --- |
| Handwritten enum switch | Repository benchmark code | Directional lower-bound reference for matched state changes |
| TedToolkit StateMachine | Current workspace | Generated enum routing, guards, lifecycle calls, and events |
| Stateless | 5.20.1 | Runtime-configured fluent state-machine model used by the pinned adapter |
| Appccelerate.StateMachine | 6.0.0 | Runtime definition and machine model |

The adapters verify matched state, guard, and lifecycle outcomes before timing. They do not expose identical feature sets or construction models, so the result does not rank hierarchy, persistence, graph export, thread safety, dynamic configuration, or other unmatched capabilities.

## Latest result summary

Environment: BenchmarkDotNet 0.15.8, .NET 10.0.11 x64, Windows 11 25H2, Intel Core i7-12700H; two launches, five warmups, 15 measured iterations, 250 ms target iteration time. Setup and configuration are outside execution measurements.

### Execution

| Workload | Handwritten | TedToolkit | Stateless 5.20.1 | Appccelerate 6.0.0 |
| --- | ---: | ---: | ---: | ---: |
| Observable toggle | ~0.09 ns* / 0 B | 30.40 ns / 0 B | 300.60 ns / 1,208 B | 282.60 ns / 1,544 B |
| Guard + exit + entry | ~0.19 ns* / 0 B | 32.36 ns / 0 B | 376.87 ns / 1,208 B | 414.91 ns / 1,544 B |
| Capability query | ~0.06 ns* / 0 B | 4.87 ns / 0 B | 143.24 ns / 616 B | Not represented |

In this matched workload TedToolkit's generated path is materially below the two runtime-configured adapters and performs no per-operation managed allocation. The exact ratios are workload-specific, and some comparison-library confidence intervals are wide.

*The handwritten execution/query means are at or near BenchmarkDotNet's empty-method overhead. They are retained only as directional lower bounds; ratios against them are not meaningful.

### Construction

| Implementation | Mean | Allocated |
| --- | ---: | ---: |
| Handwritten | 4.01 ns | 24 B |
| TedToolkit | 3.87 ns | 24 B |
| Stateless | 287.67 ns | 2,456 B |
| Appccelerate | 358.38 ns | 1,856 B |

TedToolkit and handwritten confidence intervals overlap, so this run does not establish a difference between them. Construction is not fully equivalent: TedToolkit generates routing at compile time, Stateless configures each measured instance, and the Appccelerate adapter reuses a definition before constructing and starting an instance.

### Follow-up after reentry protection

A TedToolkit-only follow-up used the same launch, warmup, iteration, and iteration-time settings after callback-boundary reentry protection was added:

| Workload | Mean | Allocated |
| --- | ---: | ---: |
| Observable toggle, no subscribers | 32.06 ns | 0 B |
| Guard + exit + entry with callback scopes | 55.87 ns | 0 B |

The value-type callback scope keeps both paths allocation-free. The guard/lifecycle path pays for entering and disposing protection around enabled callbacks. Stateless and Appccelerate were not rerun, so these follow-up numbers must not be used to calculate new cross-library ratios.

## Workloads

| Benchmark | Observable work |
| --- | --- |
| `TransitionBenchmarks` | Toggle between two states and publish the adapter's equivalent observable transition behavior |
| `GuardAndLifecycleBenchmarks` | Evaluate a guard, run exit behavior, commit state, and run entry behavior |
| `CanFireBenchmarks` | Query whether the toggle trigger is currently accepted, where the adapter exposes an equivalent operation |
| `ConstructionBenchmarks` | Construct/setup each adapter using its native model |

These are deliberately small hot-path measurements. Applications dominated by database, network, logging, or user callback work will see those costs dominate the state-machine dispatch cost.

## Correctness gate

Run adapter verification before collecting timings:

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks --configuration Release -- --verify
```

The verification checks equivalent state changes, guard decisions, and lifecycle effects. A passing adapter check supports workload comparability; it does not imply that the libraries offer the same broader behavior.

## Reproduce the comparison

From the repository root with the .NET 10 SDK:

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks --configuration Release --no-build -- --filter '*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250
```

BenchmarkDotNet runs timed cases in separate child processes. Stop other builds and CPU-heavy work before measuring. Treat a new run as a new evidence snapshot: record the runtime, host, package versions, settings, confidence intervals, and any adapter changes before comparing it with this one.

## How to interpret the result

- Choose TedToolkit StateMachine when enum states and routes are static, compile-time diagnostics are valuable, and generated direct dispatch matches the required feature set.
- Choose handwritten code when the lifecycle is tiny and minimum abstraction is more valuable than generated trigger APIs, guards, diagnostics, and events.
- Choose Stateless or Appccelerate when their runtime configuration model or broader feature set fits the application better.
- Choose a durable workflow/state solution when transitions must persist, resume after failure, coordinate services, or support operator-driven topology; those requirements are outside this library.
- Do not generalize these microbenchmarks to workloads dominated by user callbacks, I/O, locks, or external side effects.

## Evidence

The [full comparison](library-comparison.md) contains the original tables, construction discussion, follow-up interpretation, package versions, and links identifying the raw BenchmarkDotNet report paths used for the recorded analysis.

Generated benchmark artifacts are local evidence and are not part of the runtime package. The benchmark-only references to Stateless, Appccelerate, and BenchmarkDotNet do not become StateMachine runtime dependencies.
