# TedToolkit.Orchestration

Strongly typed, compile-time orchestration for .NET 10. The repository contains two independent libraries: **Pipeline** generates execution code for a static dependency graph, while **StateMachine** generates enum-state routing, guards, lifecycle callbacks, and transition notifications.

[![Build](https://github.com/TedToolkit/TedToolkit.Orchestration/actions/workflows/build.yml/badge.svg?branch=development)](https://github.com/TedToolkit/TedToolkit.Orchestration/actions/workflows/build.yml)

Both runtime packages include their matching analyzer and source generator. Consumers write declarations; the compiler validates them and emits direct, typed execution code.

## Choose a package

| Package | Use it when | Main result |
| --- | --- | --- |
| `TedToolkit.Orchestration.Pipeline` | A fixed in-process operation has typed steps, dependencies, independent branches, retries, timeouts, cancellation, or DI inputs | A generated executor with typed parameters and results |
| `TedToolkit.Orchestration.StateMachine` | An enum-backed model has statically known triggers, guarded routes, entry/exit behavior, and observable transitions | A generated machine with strict, `Try...Async`, and `Can...Async` trigger APIs |

Start with the [Pipeline playground](playground/TedToolkit.Orchestration.Pipeline.Playground/Program.cs) or [StateMachine playground](playground/TedToolkit.Orchestration.StateMachine.Playground/Program.cs) when you want a complete runnable example.

## Why this library exists

Small orchestration code often begins as a few method calls and gradually accumulates dependency plumbing, task coordination, cancellation rules, retry loops, guard selection, lifecycle ordering, and result transport. Handwritten code can remain the fastest and simplest answer, but maintaining those semantics repeatedly is expensive and error-prone.

General-purpose workflow and state-machine libraries solve broader problems through runtime models. That flexibility is valuable when graphs must be assembled dynamically, workflows must survive a process, messages need backpressure, or state topology changes at runtime. It is unnecessary overhead when the topology is already known to the compiler.

TedToolkit.Orchestration occupies the middle ground:

- declarations remain ordinary C# and are validated during compilation;
- generated control flow uses direct typed calls rather than runtime graph traversal or reflection-based activation;
- unsupported or ambiguous declarations fail with actionable diagnostics instead of falling back to a different runtime meaning;
- generated code keeps dependencies, cancellation, retry, lifecycle, and ownership rules inspectable;
- consumers pay for enabled behavior rather than a universal runtime engine.

The goal is not to replace every workflow or state-machine library. It is to make **static, in-process orchestration** safer and less repetitive without hiding its runtime cost.

See the approved [product intent](docs/product/README.md), [Pipeline design principles](docs/principles/README.md), [Pipeline architecture](docs/architecture/pipeline-system.md), and [StateMachine architecture](docs/architecture/state-machine-system.md).

## Why not use something else?

The right choice depends on the problem boundary. The alternatives below are not interchangeable feature sets; they are included because they represent useful neighboring approaches.

| Approach | Prefer it when | TedToolkit's different trade-off |
| --- | --- | --- |
| Handwritten orchestration | The flow is tiny, unique, and minimizing every abstraction cost matters more than reusable policies or compile-time graph checks | Generates the repetitive coordination while retaining typed, direct execution |
| WorkflowFramework | Its runtime typed-pipeline/workflow model and broader workflow abstractions fit the application | Resolves a static graph at compilation and emits a dedicated executor |
| PipelineNet | Middleware-style runtime composition is the desired programming model | Models typed data dependencies rather than an invocation middleware chain |
| TPL Dataflow | Streaming, buffering, backpressure, and multiple messages in flight are first-class requirements | Targets request-style execution of one static dependency graph |
| Stateless or Appccelerate.StateMachine | Runtime configuration and their broader state-machine feature sets are more important than generated direct dispatch | Validates enum routes at compilation and keeps the common trigger path small |
| Durable/distributed workflow engines | Work must persist, resume, coordinate services, or survive process failure | Deliberately remains in-process and non-durable |

The benchmark adapters compare only matched micro-workloads. They do not erase differences in features, lifecycle, streaming behavior, persistence, or configuration models.

## Quick start: Pipeline

Add `TedToolkit.Orchestration.Pipeline` from the feed that contains your build. The package targets .NET 10 and brings its analyzer with it. Add `Microsoft.Extensions.DependencyInjection` when using Microsoft's container.

```csharp
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.Orchestration.Pipeline;
using TedToolkit.Orchestration.Pipeline.Attributes;

var pipeline = new Sum.Pipeline();
var results = pipeline.Execute(leftValue: 40);

Console.WriteLine(results.Add); // 42

[CompositeStep]
public readonly ref partial struct Sum(int leftValue)
{
    private void Configuration(StepGraph pipeline)
    {
        var left = pipeline.Value(leftValue);
        var right = pipeline.Value(2);
        var add = pipeline.Add(left, right);
    }
}

internal readonly ref partial struct Value(int value) : IStep<int>
{
    public int Execute(CancellationToken token = default) => value;
}

internal readonly ref partial struct Add(int a, int b) : IStep<int>
{
    public int Execute(CancellationToken token = default) => a + b;
}
```

The Composite primary-constructor parameters are its typed inputs. The generator recognizes the fixed value `2`, binds both results into the named `add` node, and generates the typed `Results.Add` property. An asynchronous child changes the entry point to `ExecuteAsync`; completion-only callers use `ExecuteWithoutResults` or `ExecuteWithoutResultsAsync`. A Composite can also be registered in another `StepGraph`; only a root caller uses the generated nested `Pipeline` facade.

Node-specific orchestration stays in `Configuration`:

```csharp
var prepare = pipeline.Prepare();
var work = pipeline.Work()
    .DependsOn(prepare)
    .WithRetry(2)
    .WithTimeout(5_000)
    .WithDisplayName("Main work");
```

`DependsOn` waits for successful completion without transporting data. Retry, timeout, and display identity belong only to that registration; omitted policy means zero retries and infinite timeout. Every Step receives a generated required `DisplayName` property. Mark a Step with `[StepLogger]` when it also needs a generated `ILogger` whose category contains the Step type and display name.

Retrying a Composite registration reruns its complete child graph, so already-completed child side effects may occur again. A service-requiring root `Pipeline` retains the caller-provided `IServiceProvider`; registering that facade as a singleton therefore explicitly selects the root provider, and the library neither creates nor repairs scopes.

Factory overload resolution distinguishes same-named Steps when their signatures differ. If public Steps from different assemblies have the same name and signature, call the generated namespace-qualified factory class explicitly (for example, `Alpha_IncrementExtensions.Increment(pipeline, value)`) to select the exact Step symbol.

Pipeline also supports:

- independent branches that start without waiting for unrelated work;
- constructor parameters resolved from ordinary or keyed DI via `[FromServices]`;
- per-node retry and cooperative timeout through Configuration modifiers;
- control-only dependencies and immutable display metadata;
- caller cancellation and draining of all work started by a parallel invocation;
- typed intermediate results without an untyped runtime result store.

Configuration must remain statically analyzable: register each step in an unconditional statement, declare dependencies before consumers, and move dynamic behavior into a step. Read [declaration and execution semantics](docs/architecture/named-executors.md) for the full contract.

## Quick start: StateMachine

Add `TedToolkit.Orchestration.StateMachine` from the feed that contains your build. Declare an enum, mark a sealed partial class, and describe each trigger with attributes:

```csharp
using TedToolkit.Orchestration.StateMachine;

var door = new DoorMachine();
door.TransitionCompleted += (_, transition) =>
    Console.WriteLine($"{transition.Source} -> {transition.Destination}");

await door.OpenAsync();

public enum DoorState
{
    Closed,
    Open,
}

[StateMachine<DoorState>(DoorState.Closed)]
public sealed partial class DoorMachine
{
    [TransitionTo(DoorState.Open, DoorState.Closed)]
    public partial ValueTask OpenAsync();

    [TransitionTo(DoorState.Closed, DoorState.Open)]
    public partial ValueTask CloseAsync();
}
```

For each declared trigger the generator supplies:

- the strict trigger, which throws `TriggerRejectedException` when no route is accepted;
- `Try...Async`, which returns `TriggerResult<TState>` for expected rejection;
- `Can...Async`, which evaluates state and guards without running lifecycle callbacks;
- constructors for an explicit initial state and, when specified in the attribute, the default state.

Multiple guarded routes may use synchronous, `Task<bool>`, or `ValueTask<bool>` guards. `OnExit`, `OnEntry`, and `OnEntryFrom` callbacks may return `void`, `Task`, or `ValueTask`. The generated order is guard selection → exit → state commit → `Transitioned` → entry → trigger-specific entry → `TransitionCompleted`.

StateMachine is intentionally not a concurrency controller. It rejects same-instance trigger reentry from guards, callbacks, and event handlers, but callers still own synchronization when sharing an instance across concurrent operations. Read the [StateMachine architecture](docs/architecture/state-machine-system.md) for rejection, failure, lifecycle, and ownership semantics.

## Benchmark snapshot

These are local BenchmarkDotNet 0.15.8 measurements recorded on 2026-09-08 using .NET 10.0.11 and an Intel Core i7-12700H. They are evidence for the measured workloads, not universal rankings.

### Pipeline: four-operation yielding workloads

| Implementation | Chain mean / allocation | Diamond mean / allocation |
| --- | ---: | ---: |
| Handwritten tasks | 2.850 μs / 560 B | 2.810 μs / 720 B |
| TedPipeline | 2.895 μs / 680 B | 4.362 μs / 1,682 B |
| WorkflowFramework | 3.294 μs / 1,032 B | 8.006 μs / 4,433 B |
| PipelineNet | 5.764 μs / 2,847 B | Not represented |
| TPL Dataflow | 13.109 μs / 2,161 B | 11.800 μs / 2,314 B |

The chain intervals overlap, so the small difference between handwritten and TedPipeline is inconclusive. The diamond workload exposes additional generated coordination: TedPipeline was slower and allocated 962 B more than the lower-abstraction handwritten baseline in this run. Pure synchronous completion-only execution measured 11.645 ns and 0 B versus 2.659 ns and 0 B handwritten.

A focused 2026-09-08 Composite run measured flat versus one-boundary nested execution at
5.235 ns / 0 B versus 6.264 ns / 0 B synchronously, 47.79 ns / 288 B versus
55.80 ns / 360 B for completed Tasks, and 1.793 μs / 440 B versus 1.863 μs / 560 B
for yielding Tasks. The synchronous and yielding confidence intervals overlapped. See the
[Composite Step benchmark record](benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks/composite-step-results.md)
for the exact commands and limitations.

[Method, complete tables, confidence intervals, versions, and limitations](benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks/composite-step-results.md)

### StateMachine: generated direct dispatch

| Workload | TedToolkit | Stateless 5.20.1 | Appccelerate 6.0.0 |
| --- | ---: | ---: | ---: |
| Observable toggle | 30.40 ns / 0 B | 300.60 ns / 1,208 B | 282.60 ns / 1,544 B |
| Guard + exit + entry | 32.36 ns / 0 B | 376.87 ns / 1,208 B | 414.91 ns / 1,544 B |
| Capability query | 4.87 ns / 0 B | 143.24 ns / 616 B | Not represented |

A later TedToolkit-only run that included callback-boundary reentry protection measured 32.06 ns / 0 B for the observable toggle and 55.87 ns / 0 B for guard plus lifecycle callbacks. The comparison libraries were not rerun, so no cross-run ratio is claimed.

[Method, construction results, complete tables, and limitations](benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks/library-comparison.md)

## Scope and constraints

TedToolkit.Orchestration is designed for:

- statically visible, in-process graphs and enum-state routes;
- applications that can recompile when the bundled generator changes;
- caller-owned DI scopes, cancellation boundaries, and shared-instance synchronization;
- cooperative cancellation rather than forced termination;
- generated code that can be inspected and trimmed when consumer code does not root declarations.

It does not provide:

- persisted or distributed workflows;
- runtime graph construction or mutation;
- streaming throughput, buffering, or backpressure;
- a trigger queue, implicit locking, or framework-owned StateMachine concurrency;
- automatic compensation when an entry callback fails after state commit.

## Diagnostics

Pipeline diagnostics use the `TTP` prefix. They reject invalid graph shapes, type/nullability mismatches, unsupported Step contracts, modifier errors, dynamic declarations, and generated-name collisions. `TTP014`, `TTP015`, and `TTP017` flag declaration APIs used outside a Composite configuration.

StateMachine diagnostics use `TTSM001` for invalid declarations or generated-member collisions and `TTSM002` for direct `State` assignment that would bypass generated lifecycle behavior.

Diagnostic severity can be configured through standard `.editorconfig` settings.

## Development

### Prerequisites

- .NET 10 SDK
- Git with submodule support

Clone with submodules, then run the repository's TedToolkit build pipeline:

```shell
git clone --recurse-submodules https://github.com/TedToolkit/TedToolkit.Orchestration.git
cd TedToolkit.Orchestration
dotnet run --project Build/Build.csproj --configuration Release
```

Focused commands:

```shell
dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests --configuration Release
dotnet run --project tests/TedToolkit.Orchestration.StateMachine.Tests --configuration Release
dotnet run --project playground/TedToolkit.Orchestration.Pipeline.Playground --configuration Release
dotnet run --project playground/TedToolkit.Orchestration.StateMachine.Playground --configuration Release
```

Benchmark correctness checks run the adapters without timing them:

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks --configuration Release -- --verify
```

Read the benchmark-specific READMEs before collecting measurements:

- [Pipeline benchmarks](benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks/README.md)
- [StateMachine benchmarks](benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks/README.md)

## Repository map

| Area | Responsibility |
| --- | --- |
| `src/TedToolkit.Orchestration.Pipeline*` | Pipeline runtime, analyzer, and source generator |
| `src/TedToolkit.Orchestration.StateMachine*` | StateMachine runtime, analyzer, and source generator |
| `tests/` | Focused behavioral, generation, diagnostic, lifecycle, and packaging tests |
| `playground/` | Runnable consumer examples and trimming verification |
| `benchmarks/` | Correctness-checked adapters, microbenchmarks, and recorded interpretations |
| `docs/product/` | Durable product purpose and boundaries |
| `docs/principles/` | Recurring engineering defaults |
| `docs/architecture/` | Current compile-time and runtime semantics |
| `Build/` | Repository configuration for the shared TedToolkit build pipeline |

## License

Licensed under LGPL-3.0-only. See [COPYING.LESSER](COPYING.LESSER) and [COPYING](COPYING).
