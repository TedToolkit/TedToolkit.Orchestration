# State machine compile-time and execution architecture

- Status: Active
- Owner: library maintainers
- Scope and system boundary: the StateMachine runtime package, bundled analyzer/source generator, generated consumer partial classes, consumer state enums, guards, and caller-owned machine instances
- Applicable product intent: None; the state-machine product direction was approved directly by the owner in the originating Codex task
- Governing principles: P8 in `docs/principles/README.md` directly governs both Pipeline and StateMachine; its other compile-time, inspectability, pay-for-use, and reject-unsupported directions remain architectural precedent here
- Related ADRs: None
- Approval source: the user approved the analyzer-first enum-state design, Attribute-marked lifecycle hooks, mixed synchronous, `Task`, and `ValueTask` guards and hooks, constructor-supplied initial state with an optional Attribute-declared default, fail-fast same-instance trigger reentry protection, caller-owned concurrency, custom public transition events with Stateless timing, and inline lifecycle calls while retaining asynchronous `ValueTask` triggers on 2026-09-05

## Current architecture

StateMachine has a compile-time control plane and a small generated runtime data plane. A consumer writes a sealed partial class marked with `[StateMachine<TState>]` and public partial trigger methods marked with transition attributes. The generator emits another partial declaration that adds `StateMachine<TState>` inheritance, a public constructor accepting the initial state, strict trigger implementations, and matching `Can...Async` and `Try...Async` methods. `[StateMachine<TState>(state)]` additionally emits a parameterless constructor chained to the state-taking constructor; omitting the argument does not emit it.

One trigger owns a statically known set of routes rather than one edge. `TransitionTo` declares a target and one or more allowed sources; repeated attributes allow source-dependent targets. A compatible `Can<Trigger>` convention guard applies to a single candidate. Explicit guard names distinguish multiple candidates from the same source, and `TransitionOtherwiseTo` supplies at most one fallback. Generated code evaluates guards for every invocation, requires at most one guarded match, and commits the selected enum value directly without reflection or a runtime graph.

Optional lifecycle behavior is attached directly to arbitrary consumer methods with `OnExit(state)`, `OnEntry(state)`, and `OnEntryFrom(state, nameof(trigger))`. The analyzer resolves the named trigger to its declared method and requires that it can enter the attributed state. A hook returns `void`, `Task`, or `ValueTask`; guards return `bool`, `Task<bool>`, or `ValueTask<bool>`. These forms may be mixed and are invoked directly or awaited in declaration order; `async void` is rejected. Hook parameters bind to trigger parameters by exact name and type, while `CancellationToken` is supplied from the trigger or as `default`. The generated execution order is guard selection, source exit, state commit, target entry, then trigger-specific target entry. Exit failure leaves the source state unchanged. Entry failure propagates after the target is committed; the library does not imply rollback of state or external side effects. Capability queries execute only state and guard checks.

The runtime base owns the current enum value through a protected setter and exposes `Transitioned` and `TransitionCompleted` for external observation. Generated triggers publish `Transitioned` after committing the target and before entry handlers, then publish `TransitionCompleted` only after all general and trigger-specific entry handlers succeed. Event payloads contain strongly typed source and destination enum values without runtime trigger-name lookup. Rejected triggers and capability queries publish nothing. Subscriber exceptions propagate at the notification point. A newly created or rehydrated machine receives its initial state through construction without publishing events. State remains an enum value and no runtime state façade or versioned state handle is exposed, so retained callers cannot hold a stale State object.

The runtime base owns one private callback-depth counter. `EnterConsumerCallback` returns a disposable `readonly struct` scope; generated guard and lifecycle invocations, plus transition-event publication when subscribers exist, use it with `using`, so disposal restores the depth on success or failure. A regular struct is required because scopes may cross asynchronous suspension; a `ref struct` cannot remain live across `await`. Every generated `Try...Async` checks the boundary before evaluating state; a strict trigger delegates to that companion. A strict or `Try...Async` trigger invoked from a guard, lifecycle hook, or synchronous event callback on the same instance throws `ReentrantTriggerException`. `Can...Async` remains queryable and may nest callback scopes without clearing an outer scope. This protection adds no lock, queue, ambient state, or cross-instance coordination, and callers remain responsible for serializing concurrent access to a shared instance.

The protected setter exists only as the cross-assembly seam used by generated derived classes. Consumer assignments to `State` bypass transition lifecycle and are rejected by the analyzer. The generator also rejects consumer members that hide the base state/events/raisers or collide with generated constructors and Trigger companion methods. These ownership checks remain compile-time-only and add no runtime dispatch.

Dependency direction is one-way: consumer declarations depend on runtime attributes; the analyzer reads consumer symbols and emits code into the consumer compilation; generated code depends on the runtime base and result/exception contracts. The runtime does not depend on analyzer implementation and does not discover declarations at runtime.

## Constraints for change design

- Preserve enum value state as the runtime identity unless a separately approved persistence and compatibility design replaces it.
- Keep route topology, state type, guard binding, generated names, and unsupported declaration rejection as compile-time concerns.
- Generated inheritance must remain additive through a partial declaration; source generation must not require consumers to write or maintain the generic base relationship.
- Keep trigger methods on the Machine and read current state for each invocation; do not expose state-specific runtime handles.
- Reserve the inherited state, event, and event-raiser names plus generated constructor and Trigger companion signatures. When the Attribute declares an initial state, also reserve the parameterless constructor. Reject consumer writes to `State`; every observable transition must flow through a generated Trigger.
- Guards may be synchronous, `Task`-based, or `ValueTask`-based, are evaluated on every query or trigger, and must be treated as side-effect-free consumer predicates. Runtime ambiguity detection remains required because arbitrary guard exclusivity cannot be proven statically.
- Public triggers remain `ValueTask`-based so synchronous and asynchronous guards and hooks share one generated surface. A synchronous trigger surface requires a separate all-synchronous execution design.
- Keep lifecycle topology, State/Trigger relationships, and parameter binding as compile-time Attribute concerns. Do not infer lifecycle roles from method names or discover hooks through reflection.
- At most one general exit, one general entry, and one trigger-specific entry may match a route. Execute general entry before trigger-specific entry; reject conflicts rather than adding ordering metadata.
- `Can...Async` must not execute lifecycle hooks. `Try...Async` and strict triggers execute the same lifecycle, and hook exceptions propagate without conversion to an expected rejection result.
- Reject strict and `Try...Async` trigger reentry from guards, lifecycle hooks, and event callbacks with `ReentrantTriggerException`. Keep `Can...Async` queryable during an active trigger and release protection through the disposable consumer-callback scope.
- Commit after exit completes. Do not automatically roll back a committed target when entry or trigger-specific entry fails, because generated state rollback cannot compensate caller-owned side effects.
- Keep lifecycle calls inline in generated trigger paths. Do not introduce shared entry/exit dispatch functions solely to reduce generated source size when they add hot-path calls.
- Publish `Transitioned` after state commit and before entry; publish `TransitionCompleted` after every entry handler succeeds. Do not publish either event for rejected triggers or capability queries.
- Keep reentry protection instance-local and fail-fast. Do not add implicit locking, ambient execution state, or a trigger queue. Callers that share one instance concurrently must provide their own synchronization.
- Reject unsupported signatures and route conflicts rather than interpreting them through reflection or a dynamic runtime fallback.
- Public attribute, generated constructor and methods, result, exception, state initialization, and concurrency semantics are consumer contracts and require compatibility review when changed.

## Decision links and exceptions

No ADR exception is active. Type-based runtime states were considered during design and rejected because retained state façades require stale-handle/version semantics without improving the persisted state model. Partial files remain the source organization mechanism while generated enum routing supplies runtime behavior.

## Review triggers

Reassess this architecture when hierarchical or parallel states, dynamic destinations, trigger identity in transition events, asynchronous external event subscribers, compensating or transactional entry behavior, multiple ordered hooks for one convention, durable trigger queues, framework-owned concurrency, state history, non-enum state identities, runtime graph mutation, or binary compatibility without consumer recompilation becomes required. Also reassess when guard evaluation must perform side effects or caller-owned synchronization proves insufficient for a measured workload.
