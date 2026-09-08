# Composite Step declarations

Status: Accepted and implemented. Owner: library maintainers. User-approved direction: 2026-09-08.

A graph is a top-level, non-generic `public` or `internal readonly ref partial struct` marked `[CompositeStep]`. It declares exactly one `private void Configuration(StepGraph steps)` method. The method is source-only: generated code analyzes it but never invokes it at runtime.

Primary-constructor parameters are the Composite's typed data inputs. Configuration must bind every leaf or nested-Composite input explicitly. Each registration creates one node; a local alias preserves node identity. `DependsOn` adds a successful-completion dependency without carrying data. `WithRetry`, `WithTimeout`, and `WithDisplayName` attach fixed behavior to that registration. There is no type-level policy fallback.

Every leaf Step is a top-level, non-generic `internal readonly ref partial struct` implementing exactly one Step interface. Generated partial context adds `required string DisplayName { get; init; }`. `[StepLogger]` additionally adds a required `ILogger`. The logger is created once when the node becomes ready, uses category `<fully-qualified type>[<display name>]`, and is reused across retries.

The generator emits direct typed construction and calls. Arguments and services are evaluated after dependencies complete and before the retry loop; retries create a fresh Step while reusing those values. Synchronous disposal completes before retry or success. An asynchronous Step returns its operation before its ref struct goes out of scope, so no ref struct is retained in an async state machine.

A Composite receives the same treatment as a Step. Its generated result is its `Results` value, and a parent calls it directly; the parent's `IServiceProvider` is forwarded only when the child graph needs services or logging, and no nested facade is allocated. Retrying a parent Composite node reruns the whole child graph, while policies inside the child remain local.

Each Composite also receives a nested `public sealed class Pipeline` for root execution. Service-free graphs get a parameterless facade. Graphs that transitively use `[FromServices]` or `[StepLogger]` get an `IServiceProvider` constructor. The facade stores but never owns or disposes the provider, and all per-invocation execution state remains local.

All-synchronous graphs expose synchronous entry points. Any asynchronous child produces Task-based entry points. Independent nodes start without waiting for unrelated work; dependent nodes await only their data and control prerequisites. The outer non-generic `Task.WhenAll` drains all started work. Cancellation is cooperative, and terminal failures cancel remaining graph work without replacing the original failure.

Unsupported syntax never falls back to runtime interpretation. Conditional registration, mutable registration locals, runtime-computed modifiers, cycles, unsafe inputs, reserved member collisions, and invalid Step/Composite shapes are compile-time errors.
