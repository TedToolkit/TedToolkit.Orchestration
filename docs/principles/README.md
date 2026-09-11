# Design principles

Scope: the runtime package, generated public surface and bundled analyzer. Owner: library maintainers. These principles follow the [approved product intent](../product/README.md), confirmed by the user on 2026-09-04. Compiler safety rules take precedence.

## P1 - Move abstraction costs to compilation

Status: Active. Strength: Required.

Resolve type compatibility, graph shape, ordering and fixed policy values during generation. Emit direct typed Step invocation, with graph-independent policy helpers in the Runtime compiler-support contract; avoid runtime graph interpretation, reflection-based activation and interface boxing in execution. This keeps composition conveniences out of the hot path. Review whenever a new runtime abstraction is proposed.

## P2 - Pay only for enabled capabilities

Status: Active. Strength: Required.

Fully synchronous graphs use direct synchronous calls. Linear chains use direct calls and awaits. Parallel graphs use typed Step tasks for dependency coordination, including synchronous consumers of asynchronous results, without Task.Run. Default policies emit no retry loops or timeout sources. Fully synchronous graphs have synchronous entry points. Completion-only execution omits result containers and collection boxing. Required business tasks, asynchronous state, concurrency coordination and explicitly collected typed results remain legitimate costs.

## P3 - Prefer value semantics for framework data

Status: Active. Strength: Default.

Use immutable argument/marker values where their lifetimes permit. Leaf Steps are static functions rather than runtime Step objects. Avoid hiding allocations through object/interface storage or captures. Step identity belongs to the compile-time graph: a marker alias preserves its source and a new registration creates a distinct source. The shared Builder and argument markers are empty readonly values; builder aliases and escape are rejected by the generator. Configuration is source-only. Generated execution retains evaluated arguments in local variables for reuse across retries. Avoid introducing runtime graph collections when configuration can be resolved during generation.

## P4 - Generate inspectable execution code

Status: Active. Strength: Default.

Keep control flow close to sensible handwritten code. Call Step methods directly, keep intermediate values typed and preserve independent readiness through explicit upstream tasks. Avoid generated branch methods and local scheduling functions. Use TedToolkit.RoslynHelper for source composition and prefer symbol/System.Type-based DataType construction. Review when optimizations add indirection or obscure ownership.

## P5 - Demonstrate performance without weakening semantics

Status: Active. Strength: Required.

Measure runtime and allocation costs against equivalent handwritten work. Separate build-time costs, successful execution, policy paths and result collection. Never infer zero allocation from the words struct or source generator; never improve benchmarks by removing required cancellation, dependency or cleanup behavior. Keep source-bound historical evidence when implementations change.

## P6 - Reject unsupported declarations at compilation

Status: Active. Strength: Required. Owner: library maintainers. Approved by the user on 2026-09-05. Review whenever a declaration cannot be represented faithfully by the compile-time graph model.

Accept a declaration only when the analyzer can bind its symbols, identity, types, ordering, lifetimes and execution meaning statically. Otherwise emit an actionable diagnostic and do not generate a partial executor with guessed or degraded behavior. Use symbol identity rather than matching names. Reject dynamic or conditional graphs, builder escape, mutable node handles, ambiguous Step contracts, unsupported ref-like inputs, invalid policies and generated-name collisions instead of interpreting them at runtime.

This protects type safety and keeps source order, dependency identity, retry semantics and cleanup aligned with what the declaration appears to mean. Supporting a new declaration shape requires evidence that its semantics are statically deterministic. A runtime fallback or partially interpreted graph requires an accepted ADR.

## P7 - Keep runtime ownership explicit

Status: Active. Strength: Required. Owner: library maintainers. Approved by the user on 2026-09-05. Review whenever the pipeline would retain services, Steps, tasks or cleanup ownership beyond one invocation.

The caller owns the service provider, service scope and invocation cancellation boundary. A Leaf function invocation owns one attempt; asynchronous work owns the state and cleanup it needs until its returned Task completes. The generated executor drains all work it starts before the invocation completes. Resolve services once per node invocation without disposing them, call the Leaf function for every retry, finish function-owned synchronous cleanup before retrying, and keep asynchronous cleanup inside the returned operation.

This prevents leaks, use-after-dispose failures, orphaned work and ambiguous cancellation. Cancellation remains cooperative and does not imply forced termination of synchronous code. Any framework-owned service scope, retained execution facade, detached background task or invocation-surviving execution state requires an accepted ADR and a product-intent review.

## P8 - Generate only consumer-dependent code

Status: Active. Strength: Required. Owner: library maintainers. Approved by the user on 2026-09-08. Applies to Pipeline and StateMachine. Review whenever fixed support code is proposed for generation or generated code needs a new cross-assembly seam.

Generate code only when its shape or semantics depends on consumer declarations and cannot be represented faithfully as stable compiled code. Put graph-independent algorithms, resource helpers, and reusable semantics in ordinary Runtime or analyzer source. Do not generate identical fixed support merely to bypass accessibility or hide an API; expose a narrow `[EditorBrowsable(EditorBrowsableState.Never)]` compiler-services contract when generated consumer code needs cross-assembly access.

This keeps generated output small and inspectable, avoids duplicating implementation into every consumer assembly, and gives fixed behavior one tested implementation. A generated fixed helper requires an accepted exception demonstrating why ordinary compiled code cannot preserve the required semantics or performance.

## Exceptions

For Required principles, obtain the user's approval and record a narrowly scoped ADR before an intentional material deviation. Explain deviations from Default principles in the change design; use an ADR for enduring exceptions. Revisit the principles when the approved product intent changes, not merely to justify an implementation shortcut.
