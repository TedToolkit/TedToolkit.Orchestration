# TedToolkit.Orchestration

A .NET 10 compile-time orchestration library. Pipeline generates strongly typed execution from static step relationships; StateMachine generates state storage, trigger routing and guard selection from partial declarations. Each runtime package includes its matching generator and diagnostics.

## Why use it?

- **Less coordination code.** Describe dependencies once; generated execution shares upstream results, starts independent async work concurrently, and handles retry, cooperative timeout and cancellation.
- **Feedback at build time.** Typed inputs and results, nullability checks and graph diagnostics catch invalid wiring. Usage diagnostics flag declaration APIs called as runtime work.
- **Small runtime machinery.** No runtime graph traversal, reflection-based Step activation or interface boxing. Synchronous graphs generate synchronous methods; default asynchronous Steps can reuse business Tasks.
- **Inspectable output.** Generated code uses direct Step calls and ordinary tasks. Every attempt constructs a fresh ref-struct Step; typed Results expose intermediate values by name.

Use it for static, in-process orchestration. It does not provide durable workflows, distributed scheduling, streaming backpressure or graphs assembled dynamically at runtime. See the [product intent](docs/product/README.md).

Performance depends on the workload. See the [latest comparison](benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks/retry-exhaustion-benchmark.md) against handwritten code, WorkflowFramework, PipelineNet and TPL Dataflow, including allocation costs and measurement limits.

## State machine quick start

Reference `TedToolkit.Orchestration.StateMachine`. A machine declares its enum type in `[StateMachine<TState>]`; the generated partial declaration adds `StateMachine<TState>` and a constructor that accepts the initial state, so consumers do not write the base class. Supplying a state to the attribute additionally generates a parameterless constructor that starts there.

```csharp
using TedToolkit.Orchestration.StateMachine;

var order = new OrderMachine();
order.Transitioned += (_, transition) =>
    Console.WriteLine($"Committed: {transition.Source} -> {transition.Destination}");
order.TransitionCompleted += (_, transition) =>
    Console.WriteLine($"Completed: {transition.Source} -> {transition.Destination}");
await order.SubmitAsync();

public enum OrderState
{
    Draft,
    Returned,
    Reviewing,
    Approved,
    Rejected,
    ManualReview,
}

[StateMachine<OrderState>(OrderState.Draft)]
public sealed partial class OrderMachine
{
    public int ItemCount { get; set; }

    [TransitionTo(OrderState.Reviewing, OrderState.Draft, OrderState.Returned)]
    public partial ValueTask SubmitAsync();

    private bool CanSubmit() => ItemCount > 0;

    [TransitionTo(OrderState.Approved, OrderState.Reviewing, Guard = nameof(CanApprove))]
    [TransitionTo(OrderState.Rejected, OrderState.Reviewing, Guard = nameof(CanReject))]
    [TransitionOtherwiseTo(OrderState.ManualReview, OrderState.Reviewing)]
    public partial ValueTask ReviewAsync(int score);

    private bool CanApprove(int score) => score >= 80;
    private bool CanReject(int score) => score < 40;

    [OnExit(OrderState.Draft, OrderState.Returned)]
    private void LeaveEditableState() { }

    [OnEntry(OrderState.Reviewing)]
    private void EnterReviewing() { }

    [OnEntryFrom(OrderState.Reviewing, nameof(SubmitAsync))]
    private ValueTask EnterReviewingFromSubmitAsync() => ValueTask.CompletedTask;
}
```

Omit the attribute argument when every caller should select the state explicitly:

```csharp
[StateMachine<OrderState>]
public sealed partial class RestoredOrderMachine
{
    // Trigger declarations...
}

var restored = new RestoredOrderMachine(savedState);
```

Even when the attribute supplies a default, the generated `OrderMachine(OrderState initialState)` constructor remains available for restoring or explicitly selecting another state.

`TransitionTo` takes the target first and one or more allowed source states after it. A single candidate automatically uses a compatible `Can<Trigger>` method, which may return `bool`, `Task<bool>`, or `ValueTask<bool>`. Multiple candidates from the same source require explicit, mutually exclusive guards; `TransitionOtherwiseTo` is the fallback when none accepts the invocation. Guards bind trigger parameters by name and exact type.

The declared trigger is strict and throws `TriggerRejectedException` when the current state or guards reject it. The generator also supplies `CanSubmitAsync` and `TrySubmitAsync`; the latter returns `TriggerResult<TState>` for expected rejection without throwing. Pass a new or persisted enum value to the generated constructor.

A machine rejects nested triggers invoked from its guards, lifecycle hooks, or transition-event callbacks. Both strict triggers and `Try...Async` throw `ReentrantTriggerException`; `Can...Async` remains available as a side-effect-free query. Callback protection is cleared even when user code throws. This is deliberately not synchronization: there is no lock or queue, and callers still own concurrent access to a shared instance.

Optional behavior is attached to arbitrarily named methods with `OnExit(state)`, `OnEntry(state)`, and `OnEntryFrom(state, nameof(trigger))`. The analyzer verifies that an `OnEntryFrom` trigger exists and can enter the attributed state. Hooks return `void`, `Task`, or `ValueTask`, and their parameters are selected from the trigger by exact name and type; `CancellationToken` is passed automatically. Synchronous and asynchronous guards and hooks may be mixed in one transition; `async void` hooks are rejected.

The generated order is guard selection → source exit → state commit → `Transitioned` → target entry → trigger-specific target entry → `TransitionCompleted`, matching Stateless notification timing. Both public events carry a strongly typed `StateTransition<TState>` with source and destination values. When both entry forms match, `OnEntry` runs before `OnEntryFrom`. `Can...Async` and rejected triggers publish no events. An exit exception keeps the source state and publishes nothing; an entry exception propagates with the target already committed and only `Transitioned` published. Subscriber exceptions propagate at their notification point. One handler of each lifecycle kind may match a route, so ordering metadata is unnecessary.

State machine declarations must be sealed, top-level, non-generic partial classes without an explicit base class. The generator supplies `Machine(TState initialState)`; additional consumer constructors must chain to it or directly to `base(initialState)`. Trigger declarations remain public partial `ValueTask` methods with by-value parameters and an optional final `CancellationToken`; synchronous public triggers are not supported. Invalid or ambiguous static declarations produce `TTSM001` and no machine implementation.

State changes belong to generated triggers. Assigning the inherited `State` property in consumer source produces `TTSM002`, because a direct write would bypass lifecycle handlers and transition events. `State`, `Transitioned`, `TransitionCompleted`, their protected raisers, generated execution members, generated `Try...Async` / `Can...Async` companions, and the generated initial-state constructor are reserved surfaces; rename conflicting consumer members.

In the current local .NET 10 follow-up measurement, a generated observable toggle with reentry protection and no event subscribers measured 32.06 ns with 0 B allocated. The latest full comparison measured Stateless at 300.60 ns / 1,208 B and Appccelerate at 282.60 ns / 1,544 B in the same workload, in an earlier run. See the [full StateMachine comparison](benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks/library-comparison.md) for guarded transitions, capability queries, construction, raw reports, versions, and measurement limits.

## Pipeline quick start

Reference `TedToolkit.Orchestration.Pipeline` from a feed containing your build. Install `Microsoft.Extensions.DependencyInjection` to use Microsoft's container. The package targets .NET 10; consumers do not need interceptor configuration or a preview language setting.

```csharp
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.Orchestration.Pipeline;
using TedToolkit.Orchestration.Pipeline.Attributes;

using var services = new ServiceCollection().BuildServiceProvider();
var executor = new ReportPipeline(services);
var results = await executor.ExecuteAsync(leftValue: 40, formatPrefix: "sum");
Console.WriteLine(results.Format); // sum: 42
await executor.ExecuteWithoutResultsAsync(leftValue: 10, formatPrefix: "again");

public sealed partial class ReportPipeline : Pipeline
{
    protected override void Configuration(Builder pipeline)
    {
        var left = pipeline.Delay();
        var right = pipeline.Delay(2);
        var sum = pipeline.Add(left, right);
        var format = pipeline.Format(sum);
        pipeline.Write(format);
    }
}

[StepPolicy(RetryCount = 1, TimeoutMilliseconds = 2000)]
internal readonly ref struct Delay(int value) : IAsyncStep<int>
{
    public Task<int> ExecuteAsync(CancellationToken token = default) => RunAsync(value, token);
    private static async Task<int> RunAsync(int value, CancellationToken token)
    {
        await Task.Delay(10, token);
        return value;
    }
}
internal readonly ref struct Add(int a, int b) : IStep<int>
{
    public int Execute(CancellationToken token = default) => a + b;
}
internal readonly ref struct Format(int value, string prefix) : IStep<string>
{
    public string Execute(CancellationToken token = default) => $"{prefix}: {value}";
}
internal readonly ref struct Write(string value) : IStep
{
    public void Execute(CancellationToken token = default) => Console.WriteLine(value);
}
```

Inherit `Pipeline` and override `protected void Configuration(Builder pipeline)`. `Pipeline.Builder` is a single runtime type shared by all pipelines. The generator supplies Step extension methods, a public constructor taking `IServiceProvider`, typed `Results` and both execution methods. The class must be a non-generic, top-level partial class without an explicit constructor. Configuration is analyzed at compile time and is never invoked by the generated executor. Constants are mirrored into generated Step methods; other argument expressions are evaluated once when that Step becomes ready in each invocation. Seal the concrete pipeline class or mark the method `protected sealed override`: further overrides would replace configuration while leaving the generated execution graph unchanged, so the generator rejects them.

Existing `Configure(Builder pipeline, ...)` declarations remain supported, including extra constructor-time parameters. These classes must also explicitly inherit the runtime Pipeline base. The generator checks symbol identity along the inheritance chain and does not add a base type to generated partial declarations; unrelated classes with similarly named configuration methods are ignored. Use exactly one configuration entry point per pipeline. The override uses the single `Pipeline.Builder` parameter required by the base contract. Import `TedToolkit.Orchestration.Pipeline` to bring the generated Step extensions into scope.

## Bindings and names

| Configuration argument | Meaning |
| --- | --- |
| Omitted, or `default(StepArgument<T>)` | A required execution parameter |
| A fixed value | Constants are mirrored; expressions are evaluated once per Step invocation and reused by retries |
| A preceding node handle | That node's result for the current invocation |
| Step constructor parameter `[FromServices] IService service` | Resolved from DI; absent from configuration and execution parameters |
| Step constructor parameter `[FromServices(key: "blue")] IService service` | Resolved from keyed DI; absent from configuration and execution parameters |

`[FromServices]` and `[FromServices(key: null)]` resolve ordinary services. Any non-null string (including `""`) resolves that exact key using `GetRequiredKeyedService`; a missing keyed registration fails without falling back to an ordinary service. Register it with, for example, `services.AddKeyedScoped<IFormatter, Formatter>("blue")`.

All non-service factory parameters are optional `StepArgument<T>` values. Their generic type checks fixed values and dependencies. Upstream types must match exactly, including nullability. Omitting an argument exposes it even when the Step constructor itself declares a default. Use `default(int)` or `(string?)null` for a fixed default/null value; an untyped `default` leaves the slot unbound.

Execution parameters follow node registration order, then constructor parameter order. Names combine the node name and original parameter name: `load` + `path` becomes `loadPath`. Unnamed nodes use the Step type and zero-based registration index: `LoadStep0` + `path` becomes `loadStep0Path`. A copied handle retains the original name and identity. Naming collisions are diagnostics; rename the nodes to resolve them. Different source parameters are separate execution arguments even when their types match.

Keep each registration in its own unconditional statement in Configuration (or legacy Configure). Declare upstream nodes before their consumers. Local aliases of node handles are supported; reassignment, builder aliases/escape, conditional registration, loops and early returns are rejected. Initialized local values are mirrored once per consuming Step, in declaration order, before its argument expressions. Unused locals are not evaluated; values shared across several Steps are evaluated separately for each consuming Step. Use an upstream Step when computation must be shared. Standalone executable statements, local functions and mutable/ref locals are rejected; put behavior in a Step or a class helper method. Step factory names must be unique among the available generated extensions.

For existing pipelines that take extra constructor-time configuration inputs, the legacy Configure form remains available:

```csharp
private void Configure(Builder pipeline, int offset)
{
    var sum = pipeline.Add(b: offset);
}
// Generated constructor: ExamplePipeline(IServiceProvider services, int offset)
// Generated execution parameter: int sumA
```

## Execution and results

The generated entry points depend on the configured Step contracts:

| Graph | With typed results | Completion only |
| --- | --- | --- |
| All Steps synchronous, including an empty graph | Results Execute(...) | void ExecuteWithoutResults(...) |
| At least one asynchronous Step | Task<Results> ExecuteAsync(...) | Task ExecuteWithoutResultsAsync(...) |

Both entries take the same inputs and optional cancellation token. Results is a generated readonly struct with a typed property for each result-bearing node. Resultless Steps execute without adding result properties. Synchronous failures throw directly; asynchronous failures are reported through the returned task.

Each registration has a private Step method. It evaluates arguments and resolves services once, then constructs a fresh Step for each attempt. StepAttempt owns retry and timeout state. Synchronous disposal finishes before completion or retry; asynchronous cleanup belongs to the returned operation.

Parallel execution starts Step tasks directly. Each consumer awaits only its own dependencies, so unrelated work can progress independently. Execute owns a linked CancellationTokenSource: external cancellation or a terminal failure signals other Steps, and Task.WhenAll waits for all started work before returning. Cancellation is cooperative; Steps must observe the token. Concurrent exception selection follows Task.WhenAll, and cancellation exceptions can carry the linked execution token.

Without retry or timeout, serial asynchronous Steps reuse the business Task when no extra cancellation observation is needed. Completion-only execution avoids collecting the Results snapshot. Neither API promises zero allocation for asynchronous work.

The caller owns the service provider and its scope. Pipelines never dispose injected services. Configuration is never called during construction or execution; its local expressions are mirrored into generated Step methods.

## Measured performance

This local .NET 10 run used two launches per case. Values below are means for small arithmetic workloads that suspend with Task.Yield; construction and policies were excluded.

| Workload | Handwritten | TedPipeline | WorkflowFramework | PipelineNet | TPL Dataflow |
| --- | ---: | ---: | ---: | ---: | ---: |
| Chain | 3.44 μs | 3.36 μs | 3.47 μs | 10.46 μs | 13.65 μs |
| Diamond | 5.13 μs | 4.33 μs | 8.05 μs | — | 14.45 μs |

The yielding chain is comparable to handwritten timing; its interval overlaps WorkflowFramework's, so the small differences are inconclusive. Allocation is 688 B for TedPipeline versus 1,032 B for the WorkflowFramework chain adapter. The diamond allocates 1,697 B versus 4,429 B for its WorkflowFramework adapter. Its mean is below handwritten in this run, but the handwritten interval is wide and overlaps, so this does not establish a timing win.

Pure synchronous completion-only execution allocates 0 B, but averages 11.95 ns versus 2.54 ns handwritten. These adapters have different responsibilities: this is a static request-latency comparison, not a streaming or durable-workflow ranking. [Full results, raw measurements, error intervals, versions and reproducible evidence](benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks/retry-exhaustion-benchmark.md).

## Trimming

Source generators cannot delete handwritten methods from an ordinary compilation. Generated executors have no runtime reference to Configuration, so publishing with trimming can remove it. The runtime package is marked IsTrimmable and the Playground enables PublishTrimmed. A self-contained Release publish with TrimMode=link has been verified to remove Configuration from both the Playground and runtime assemblies. Reflection roots or explicit calls in a consumer can still keep a method alive.

## Step contracts and policies

| Contract | Method |
| --- | --- |
| `IStep` | `void Execute(CancellationToken)` |
| `IStep<T>` | `T Execute(CancellationToken)` |
| `IAsyncStep` | `Task ExecuteAsync(CancellationToken)` |
| `IAsyncStep<T>` | `Task<T> ExecuteAsync(CancellationToken)` |

Each Step must be an `internal ref struct` implementing exactly one contract with a public execution method. Prefer readonly when possible. Async Steps start an operation and return its Task; move async bodies into a static helper or service. The Step instance cannot survive an await. Synchronous Steps may use IDisposable or the synchronous disposal pattern. Async cleanup belongs inside the returned operation, not the Step instance.

`[StepPolicy(RetryCount = 2, TimeoutMilliseconds = 500)]` is read at generation time and controls the generated Step method. Defaults are zero retries and infinite timeout (`-1`). Retry reconstructs the Step from the same arguments; it does not repeat argument evaluation, dependency resolution or upstream work. Cancellation is not retried.

Timeout is cooperative: the executor cancels the attempt and waits for it to finish before returning or retrying. Synchronous work must observe its token to stop early. Execution cancellation wins over timeout. Parallel Steps observe the linked execution token, which is canceled by either the caller or a terminal Step failure. Unconfigured policies emit no retry loop or timeout source. Direct business Step calls perform one raw attempt; generated Step methods apply the declared policies.

## Compatibility

Consumers must recompile when updating the bundled generator. Older prototypes used generic Pipeline/builders, captured configuration and runtime graph APIs; the supported API is the named partial class shown above. Legacy Configure(Builder, ...) remains available for constructor-time inputs.

## Diagnostics and development

| ID | Severity | Meaning / action |
| --- | --- | --- |
| TTP001 | Error | Match the upstream result type and nullability to the constructor parameter |
| TTP004 | Error | Use a supported, constructible top-level Step |
| TTP008 | Error | Use supported by-value constructor parameters |
| TTP009 | Error | Keep the graph statically visible; the message identifies the invalid declaration or collision |
| TTP012 | Error | Declare Step types internal |
| TTP013 | Error | Correct the Step interface, lifecycle or retry/timeout policy |
| TTP014 | Warning | Move Builder factory calls into Configuration; they do not execute runtime work |
| TTP015 | Warning | Call a generated Execute method instead of calling Configuration |
| TTP016 | Info | A direct Step execution call bypasses its declared Retry/Timeout policy |

StateMachine diagnostics use the same build-time enforcement model:

| ID | Severity | Meaning / action |
| --- | --- | --- |
| TTSM001 | Error | Correct an invalid state-machine declaration or a member that collides with generated/base API |
| TTSM002 | Error | Change state through a generated Trigger instead of assigning `State` directly |

An unassigned Step declaration is valid: `builder.Write(value);` still registers work. Direct Step calls are also valid when one raw attempt is intentional. Adjust diagnostic severity using standard .editorconfig settings:

```ini
[*.cs]
dotnet_diagnostic.TTP016.severity = none
```

```shell
dotnet build TedToolkit.Orchestration.slnx --configuration Release
dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests --configuration Release
dotnet run --project playground/TedToolkit.Orchestration.Pipeline.Playground --configuration Release
dotnet run --project playground/TedToolkit.Orchestration.StateMachine.Playground --configuration Release
dotnet pack src/TedToolkit.Orchestration.Pipeline --configuration Release --output artifacts/packages
```

Direct project-reference consumers also reference the analyzer and import its AnalyzerDependencies.props; the Playground is a working example. Package consumers receive the analyzer and its dependencies automatically. Source composition uses TedToolkit.RoslynHelper 2026.9.4.

For contributors, the analyzer separates discovery and configuration validation (PipelineExecutorGenerator), shared semantic rules (PipelineSymbols/StepSymbols), graph reading and expression mirroring (GraphReader/ExpressionMirror), Step construction and policy emission (StepExecutionEmitter), and execution order (ExecutionEmitter). PipelineAnalyzer owns usage diagnostics.

Read the [product intent](docs/product/README.md), [design principles](docs/principles/README.md), [Pipeline architecture](docs/architecture/pipeline-system.md), [named executor decision](docs/architecture/named-executors.md), and [benchmarks](benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks/README.md). Historical benchmark snapshots describe their recorded APIs and must not be presented as current measurements.

Licensed under LGPL-3.0; see [COPYING.LESSER](COPYING.LESSER) and [COPYING](COPYING).
