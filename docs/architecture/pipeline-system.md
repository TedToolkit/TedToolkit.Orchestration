# Pipeline compile-time and execution architecture

- Status: Active
- Owner: library maintainers
- Governing decisions: [ADR-0003](../adr/ADR-0003-function-declared-leaf-steps.md) and [ADR-0004](../adr/ADR-0004-function-declared-composite-and-pipeline.md)

## Boundary

Pipeline has a compile-time control plane and a generated runtime data plane. The analyzer/source generator reads consumer `StepGraph` declarations and emits graph-specific typed code. The runtime never stores or interprets a graph.

## Compile-time model

- A Leaf is an `internal` or `public static` method marked `[Step]` in a top-level, non-generic static class.
- A Composite is an `internal` or `public static void Configuration(...)` method in a top-level, non-generic static partial class. Its first parameter is exactly `StepGraph`.
- Ordinary parameters are data inputs, `[FromServices]` parameters are DI inputs, and one optional unmarked trailing `CancellationToken` is runtime-provided.
- Generated factory extensions return empty-state `StepBuilder` handles describing typed data edges, `DependsOn` edges, retry, timeout, and one local display-name segment.
- Cross-assembly Composite binding retains the referenced assembly symbol. Identical metadata-qualified types require distinct reference aliases, which generated code carries through `extern alias`; unqualified factory ambiguity and missing aliases are compile-time diagnostics.
- Invalid or dynamic declarations produce diagnostics and no executable fallback.

Adding `[Pipeline]` to a valid Step or Configuration generates a nested root facade. The generated type is `<method name>Pipeline`, or `<Name>Pipeline` when the attribute supplies a name stem. Business/default parameters stay on Execute methods; only a required `IServiceProvider` belongs to the facade constructor.

## Runtime model

Generated code calls Step functions directly. Arguments and services are evaluated once before a node retry loop and reused across attempts. A finite timeout is cooperative through a linked token. Caller cancellation is never retried.

A nested Composite uses an IntelliSense-hidden generated prepare/execute protocol. Preparation resolves its direct services and logger once outside the parent node's retry loop; every retry executes the complete child graph. The child facade is never constructed, and its typed `Results` becomes the parent node result.

Display identity follows Composite/Step nesting. The root segment defaults to the Leaf method or Composite type name. A nested segment defaults to its registration variable name, or `<factory>#<ordinal>` when unnamed. `WithDisplayName` changes only the current segment. Non-generic `ILogger` categories include the joined path, for example `SaveSteps.Save[Root/Import/Save]`; DisplayPath is not exposed to business methods.

All-synchronous graphs remain synchronous. Asynchronous graphs keep typed node Tasks and drain all started work. Generated Pipeline methods are thin non-async forwarders into static execution, so no Composite object is constructed, captured, or boxed.

## Quality attributes

- Static safety: exact symbol and nullability binding; no reflection or untyped result store.
- Allocation transparency: no runtime graph and no nested facade allocation.
- DI ownership: the caller chooses and disposes the provider/scope.
- Trimming: Configuration is source-only and can be removed when not otherwise rooted.
- Packaging: runtime and matching analyzer ship together and consumers recompile on upgrade.

## Verification

Pipeline tests cover function contracts, node policies, input timing, DI, hierarchical logging, cleanup, cancellation, concurrency, nested Composite execution, the cross-assembly protocol, Pipeline facades, and diagnostics.

See [Composite Step declarations](named-executors.md) and [ADR-0004](../adr/ADR-0004-function-declared-composite-and-pipeline.md).
