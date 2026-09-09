# Product intent

Status: Approved. Owner: library maintainers. The Pipeline direction was approved on 2026-09-04, the StateMachine direction on 2026-09-05, and this consolidated two-product statement on 2026-09-06.

TedToolkit.Orchestration provides compile-time-generated, strongly typed orchestration for .NET developers whose pipeline topology or enum-state routes are known when the application is compiled. It replaces repeated coordination code with generated executors and machines while keeping runtime control flow, ownership, and cost close to equivalent handwritten code.

## Products and target consumers

| Product | Target consumer | Situation |
| --- | --- | --- |
| Pipeline | A .NET library or application author coordinating typed in-process operations | Steps, dependencies, independent branches, DI inputs, retry, timeout, cancellation, and result collection are static but otherwise require repeated handwritten plumbing |
| StateMachine | A .NET domain or application author modeling an enum-backed lifecycle | States, triggers, guarded routes, lifecycle callbacks, and transition observation are static and benefit from generated APIs and compiler diagnostics |

Both products assume consumers can recompile with the bundled analyzer/source generator. They are intended for application and library code, not for orchestration definitions supplied dynamically by operators or external data.

## Problem and evidence

Static orchestration has an avoidable trade-off:

- handwritten control flow minimizes abstraction, but every caller must correctly reproduce dependency ordering, cancellation, retry, result transport, guard selection, lifecycle ordering, and failure semantics;
- runtime orchestration libraries centralize those concerns, but their flexibility may require runtime graph/configuration objects, discovery, dispatch, buffering, or broader lifecycle machinery even when the topology is already fixed;
- source generation can validate the fixed topology once and emit dedicated typed control flow, but only if unsupported declarations are rejected instead of assigned surprising runtime meanings.

Repository tests exercise generation, diagnostics, execution, cancellation, retries, timeouts, lifecycle, reentry, events, and package composition. Correctness-checked benchmark adapters compare representative generated paths with handwritten code and pinned neighboring libraries. Those measurements show where generated direct execution is competitive and where handwritten code remains cheaper; they do not establish universal superiority.

## Value

The library provides:

- less repetitive coordination code without requiring a general-purpose runtime engine;
- compile-time feedback for graph shape, type/nullability binding, generated-name collisions, transition routes, guards, lifecycle callbacks, and owned state changes;
- generated, inspectable C# with direct typed calls and explicit asynchronous dependencies;
- policies and lifecycle rules whose cost is present only when the corresponding capability is used;
- analyzer and runtime versions shipped together as one consumer contract.

Performance is a measured design objective, not a promise that every generated path is allocation-free or faster than handwritten code. See the [Pipeline benchmark evidence](../../benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks/README.md) and [StateMachine benchmark evidence](../../benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks/README.md).

## Deliberate boundaries and alternatives

TedToolkit.Orchestration does not aim to provide:

- persisted or distributed workflows, process recovery, external scheduling, or human-task coordination;
- graphs or state topology assembled and mutated at runtime;
- streaming pipelines, buffering, backpressure, or sustained multi-message processing;
- a universal state-machine feature set such as hierarchical/parallel states, durable queues, graph export, or framework-owned concurrency;
- forced cancellation of synchronous work, implicit service-scope ownership, detached background work, or automatic rollback of caller-owned side effects;
- binary compatibility between arbitrarily mismatched analyzer and runtime versions.

Choose handwritten code when the flow is small and the lowest possible coordination overhead is more important than generated checks and reusable semantics. Choose a runtime pipeline, dataflow, state-machine, or durable-workflow library when its dynamic model or broader feature boundary is the real requirement. TedToolkit.Orchestration should be selected only when static topology and compile-time generation are advantages rather than restrictions.

## Observable success

The product direction is successful when:

- representative consumers can declare and execute both products without handwritten orchestration infrastructure;
- invalid or ambiguous declarations fail during compilation with actionable diagnostics;
- generated execution preserves documented dependency, cancellation, retry, lifecycle, event, reentry, and ownership semantics;
- runtime packages contain their matching analyzers and required analyzer dependencies;
- equivalent-work benchmarks report elapsed time and allocated bytes with source-bound methods and explicit limitations;
- optimizations do not weaken correctness, cleanup, cancellation, or observable lifecycle behavior.

## Downstream constraints

- Resolve static topology, symbol binding, type compatibility, and supported declaration shapes during generation.
- Keep generated control flow inspectable and avoid runtime interpretation or reflection-based activation.
- Preserve caller ownership of service scopes, cancellation, shared machine synchronization, and user-code side effects.
- Treat generated public APIs, diagnostics, runtime contracts, and analyzer packaging as one compatibility surface.
- Compare equivalent semantics before making a performance claim; separate construction, synchronous, completed-task, suspended-task, branch, policy, lifecycle, and result-collection costs.

Pipeline's governing technical defaults are recorded in [design principles](../principles/README.md); the StateMachine architecture applies their compile-time and ownership direction as precedent. Current semantics live in the [Pipeline architecture](../architecture/pipeline-system.md), [named executor design](../architecture/named-executors.md), and [StateMachine architecture](../architecture/state-machine-system.md).

## Review triggers

Revisit this intent if consumers require dynamic topology, durable/distributed execution, streaming/backpressure, framework-owned scopes or concurrency, hierarchical/parallel state semantics, binary compatibility without recompilation, independently versioned analyzers, or a target audience beyond static in-process .NET orchestration.
