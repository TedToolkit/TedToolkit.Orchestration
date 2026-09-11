# Composite Step and Pipeline declarations

Status: Accepted and implemented. Owner: library maintainers.

A Composite Step is declared by an `internal` or `public static void Configuration(...)` method in a top-level, non-generic static partial class. Its first parameter is exactly `StepGraph`; remaining ordinary parameters are execution inputs, `[FromServices]` parameters are resolved from DI, and one optional unmarked trailing `CancellationToken` is supplied by execution. `Configuration(StepGraph)` is valid.

Each registration creates one child Step node. A local variable supplies its default name; an unnamed registration uses `<factory>#<ordinal>`. `DependsOn` adds a successful-completion dependency without carrying data. `WithRetry`, `WithTimeout`, and `WithDisplayName` affect only that registration.

Every Leaf is an `internal` or `public static`, non-generic method marked `[Step]`. Supported returns are `void`, a non-ref-like result, `Task`, or `Task<TResult>`. Leaf and Configuration methods receive no generated context.

An unkeyed non-generic `[FromServices] ILogger` is created once from `ILoggerFactory` before retries. Its category is `<fully-qualified method>[<display path>]`. Nested Composite and Step segments are joined with `/`; `WithDisplayName` replaces only the segment of the node on which it is called. `ILogger<T>` remains an ordinary DI service.

Every valid Configuration becomes a StepGraph factory and can be nested in another Composite. The generated prepare/execute protocol preserves service resolution outside the parent retry loop. The parent calls this protocol directly, does not allocate a nested facade, and receives the child's generated `Results` value.

When multiple Composite factories have the same unqualified signature, call their generated namespace/assembly-qualified extension carriers explicitly. If two references contain the same metadata-qualified Composite type, give those references distinct C# aliases; generated sources preserve the exact assembly symbol and emit the corresponding `extern alias` qualifiers. Missing or duplicate aliases are diagnosed instead of producing an ambiguous call.

`[Pipeline]` is independent of Composite creation. Applying it to a valid Configuration or `[Step]` method adds a nested root execution facade. The default type is `<method name>Pipeline`; `[Pipeline(Name = "Import")]` produces `ImportPipeline`. A service-free facade has a parameterless constructor. A service-requiring facade accepts only `IServiceProvider`; all business and default parameters remain on Execute methods.

Unsupported syntax never falls back to runtime interpretation. Conditional registration, mutable registration locals, runtime-computed modifiers, cycles, unsafe inputs, reserved member collisions, and invalid Step/Configuration/Pipeline shapes are compile-time errors.
