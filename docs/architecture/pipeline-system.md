# Pipeline compile-time and execution architecture

- Status: Active
- Owner: library maintainers
- Governing decisions: [ADR-0003](../adr/ADR-0003-function-declared-leaf-steps.md), [ADR-0007](../adr/ADR-0007-natural-pipeline-return-shapes.md), and [ADR-0008](../adr/ADR-0008-method-identified-composite-functions.md)

## Boundary

Pipeline has a compile-time control plane and a generated runtime data plane. The analyzer/source generator reads consumer `StepGraph` declarations and emits graph-specific typed code. The runtime never stores or interprets a graph.

## Compile-time model

- A Leaf is an `internal` or `public static` method marked `[Step]` in a top-level, non-generic static class.
- A Composite is an arbitrarily named `internal` or `public static void` method in a top-level, non-generic static partial class. Its first parameter is exactly `StepGraph`; one owner may contain several differently named Composite functions, but same-name overloads are rejected.
- Ordinary parameters are data inputs, `[FromServices]` parameters are DI inputs, and one optional unmarked trailing `CancellationToken` is runtime-provided.
- Generated factory extensions return empty-state `StepBuilder` handles describing typed data edges, `DependsOn` edges, retry, timeout, and one local display-name segment.
- A Composite's declaration function is its identity and its `StepGraph` factory name. Each function independently owns `<method name>Result`, its same-name execution overload, and an optional Pipeline facade.
- Cross-assembly Composite binding retains the exact owner, method, result, execution overload, and referenced assembly symbol. Each requested method is validated independently, so one malformed protocol does not suppress a valid sibling. Identical metadata-qualified owner+method pairs require distinct reference aliases, which generated code carries through `extern alias`; unqualified factory ambiguity and missing aliases are compile-time diagnostics.
- Invalid or dynamic declarations produce diagnostics and no executable fallback.

Adding `[Pipeline]` to a valid Step or Composite declaration generates a nested root facade class. The generated type is `<method name>Pipeline`, or `<Name>Pipeline` when the attribute supplies a name stem. Business/default parameters stay on Execute methods; only a required `IServiceProvider` belongs to the facade constructor.

## Runtime model

Generated code calls Step functions directly. Arguments and services are evaluated once before a node retry loop and reused across attempts. A finite timeout is cooperative through a linked token. Caller cancellation is never retried.

A nested Composite uses one IntelliSense-hidden static execution overload with the same name as its declaration. Its caller resolves direct services and logger once outside the parent node's retry loop; every retry executes the complete child graph. No Step/state object or child facade is constructed, and the child's typed `<method name>Result` becomes the parent node result. Referenced assemblies are validated from the complete overload structure; no protocol Attribute or reflection fallback is used.

Display identity follows Composite/Step nesting. The root segment defaults to the Leaf method or Composite type name. A nested segment defaults to its registration variable name, or `<factory>#<ordinal>` when unnamed. `WithDisplayName` changes only the current segment. Non-generic `ILogger` categories include the joined path, for example `SaveSteps.Save[Root/Import/Save]`; DisplayPath is not exposed to business methods.

All-synchronous graphs remain synchronous. Asynchronous graphs keep typed node Tasks and drain all started work. Each generated Pipeline has one natural `Execute` or `ExecuteAsync`; resultless Leaves return `void` or `Task` and remain non-generic graph nodes without a data edge. Composite Pipeline methods are thin non-async forwarders into static execution, so no Composite object is constructed, captured, or boxed.

## Quality attributes

- Static safety: exact symbol and nullability binding; no reflection or untyped result store.
- Allocation transparency: no runtime graph and no nested facade allocation.
- DI ownership: the caller chooses and disposes the provider/scope.
- Trimming: the Composite declaration is source-only and can be removed when not otherwise rooted.
- Packaging: runtime and matching analyzer ship together and consumers recompile on upgrade.

## Verification

Pipeline tests cover function contracts, node policies, input timing, DI, hierarchical logging, cleanup, cancellation, concurrency, nested Composite execution, the cross-assembly protocol, Pipeline facades, and diagnostics.

See [Composite Step declarations](named-executors.md), [ADR-0007](../adr/ADR-0007-natural-pipeline-return-shapes.md), and [ADR-0008](../adr/ADR-0008-method-identified-composite-functions.md).
