# Composite Step and Pipeline declarations

Status: Accepted and implemented. Owner: library maintainers.

A Composite Step is declared by an arbitrarily named `internal` or `public static void` method in a top-level, non-generic static partial class. Its first parameter is exactly `StepGraph`; remaining ordinary parameters are execution inputs, `[FromServices]` parameters are resolved from DI, and one optional unmarked trailing `CancellationToken` is supplied by execution. A declaration with only `StepGraph` is valid. One class may group several differently named Composite declarations; same-name overloads are rejected.

Each registration creates one child Step node. A local variable supplies its default name; an unnamed registration uses `<factory>#<ordinal>`. `DependsOn` adds a successful-completion dependency without carrying data. `WithRetry`, `WithTimeout`, and `WithDisplayName` affect only that registration.

Every Leaf is an `internal` or `public static`, non-generic method marked `[Step]`. Supported returns are `void`, a non-ref-like result, `Task`, or `Task<TResult>`. Leaf and Composite declaration methods receive no generated context.

An unkeyed non-generic `[FromServices] ILogger` is created once from `ILoggerFactory` before retries. Its category is `<fully-qualified method>[<display path>]`. Nested Composite and Step segments are joined with `/`; `WithDisplayName` replaces only the segment of the node on which it is called. `ILogger<T>` remains an ordinary DI service.

Every valid Composite declaration becomes a StepGraph factory named after the declaration function and can be nested in another Composite. Each function independently receives one IntelliSense-hidden same-name execution overload and one nested `<method name>Result` readonly struct. That overload directly contains graph execution; there is no Step/state struct, prepare method, reserved execute name, protocol Attribute, or duplicate result-discard core. A parent resolves boundary services once outside its retry loop, calls the structural overload directly, and receives the child's named result value.

When multiple Composite functions have the same unqualified factory signature, call their generated namespace/assembly-qualified extension carriers explicitly. If two references contain the same metadata-qualified Composite owner and function, give those references distinct C# aliases; generated sources preserve the exact assembly symbol and emit the corresponding `extern alias` qualifiers. Missing or duplicate aliases are diagnosed instead of producing an ambiguous call. Each requested function protocol is validated independently, so a malformed sibling does not hide a valid Composite in the same owner.

`[Pipeline]` is independent of Composite creation. Applying it to a valid Composite declaration or `[Step]` method adds a nested root execution facade class. The default type is `<method name>Pipeline`; `[Pipeline(Name = "Import")]` produces `ImportPipeline`. A service-free facade has a parameterless constructor. A service-requiring facade accepts only `IServiceProvider`; all business and default parameters remain on its single `Execute` or `ExecuteAsync` method. Value Leaves return their business type, Composites return their named result, and `void`/`Task` Leaves preserve `void`/`Task` without a placeholder result.

Unsupported syntax never falls back to runtime interpretation. Conditional registration, mutable registration locals, runtime-computed modifiers, cycles, unsafe inputs, reserved member collisions, and invalid Step/Composite/Pipeline shapes are compile-time errors.
