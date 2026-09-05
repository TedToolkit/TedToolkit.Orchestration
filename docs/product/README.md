# Product intent

Status: Approved. Owner: library maintainers. Approval: the user's direction confirmed on 2026-09-04.

TedToolkit.Orchestration.Pipeline is a strongly typed, compile-time pipeline composition library for .NET developers. It replaces repetitive handwritten dependency coordination and execution policies with generated executors whose control flow and overhead can be compared directly with equivalent handwritten code.

Consumers describe relationships in named partial executors, bind fixed values and upstream results, let unbound slots define execution parameters, and obtain dependencies from a caller-owned service provider. They need compile-time feedback and predictable execution without building a general-purpose workflow engine.

The value is less orchestration boilerplate while keeping runtime work close to the required computation. Performance is a measured objective, not an assertion that every graph is allocation-free or matches handwritten timings.

## Boundaries

- Support statically visible graphs, independent branches, shared results, cooperative cancellation, retries and timeouts.
- Permit completion-only execution without result collection; result snapshots are an explicit cost.
- Do not provide persisted workflows, distributed scheduling, dynamic graph interpretation or forced termination of synchronous code.
- Do not retain step instances across asynchronous suspension or retries. Asynchronous operations own the state and cleanup needed until they finish; the service scope belongs to the caller.

## Success and review

Compare construction and execution separately against handwritten implementations with matching work and semantics. Record elapsed time and allocated bytes for synchronous, synchronously completed asynchronous, suspended asynchronous, branched and result-collecting executions. Preserve type safety, node identity, cancellation and cleanup behavior when optimizing.

Revisit this intent when dynamic or distributed workflows, stateful reusable steps, or a different target consumer becomes a requirement. Technical defaults are maintained in [design principles](../principles/README.md).
