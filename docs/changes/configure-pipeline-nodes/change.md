# Unify Pipeline composition as ref-struct Steps

<!-- change-format: 3 -->
<!-- workflow-profile: controlled -->
<!-- change-kind: behavior-change -->
<!-- change-status: completed -->
<!-- delivery-shape: single -->

- Priority: P1
<!-- approval-source: user explicitly approved the presented Controlled contract and requested implementation on 2026-09-08; independent design review of SHA-256 600976e1dac6542794807ed62e69e78a5a24afb468c1c5a182a1263ca77bc268 found no blocking or important findings; user approved moving graph-independent execution support into the Runtime, eliminating EmptyServices, and omitting IServiceProvider from service-free Composite paths on 2026-09-08; user approved measuring completed-Task nesting by an absolute 16 ns mean-overhead limit while retaining the relative timing rule for synchronous and yielding paths on 2026-09-08 -->
<!-- candidate-binding: workspace:5d2ca3f58088975c1186c119a656e64ef62d56c1:sha256:c1b5d8e95cea3776194587de9a49fd0fb2f8d4d2913e8c1e36bb9ecb47dd0d47 -->

<!-- section: goal-rationale -->
## Goal and rationale

Replace the separate user-authored Pipeline class model with one compile-time Step family. Leaf operations and reusable graphs are readonly ref partial struct structs; a Composite Step owns its graph and may run as the root or as one node in another Composite. A generated thin reference-type Pipeline facade supplies conventional root DI without letting the container activate, retain, or box Steps.

This delivery also completes the already-started per-node policy, control dependency, generated display context, and opt-in logging work so the breaking public model ships once rather than through a transitional Pipeline API.

## Governing decisions

- `docs/adr/ADR-0001-generated-step-context.md@5c3f7649ce8e40452495dc6bafe34d46f2cc1c65`
- `docs/adr/ADR-0002-unified-composite-step.md@e22cf33052b9402fbb9c6d7553f417b5ccfc42a0`

<!-- section: scope -->
## Scope and non-goals

- In scope: runtime marker contracts, Composite discovery, static graph validation, generated leaf context, generated root/nested execution, caller-owned DI propagation, fluent node configuration, diagnostics, package composition, migration documentation, playground, tests, trimming proof, and performance evidence.
- Removed contracts: the user-authored `Pipeline` base class, `Pipeline.Builder`, `[StepPolicy]`, `StepMetadata`, Attribute policy fallback, and compatibility aliases or adapters for those APIs.
- Non-goals: runtime or conditional graphs, persisted/distributed workflows, framework-owned scopes, DI activation of Steps, runtime-computed modifiers/display names, configurable logging categories/scopes, detached work, interface dispatch, reflection activation, or an untyped result store.
- Compatibility: intentional source and binary break. Consumers must recompile with the matching package, migrate graph classes to Composite Steps, make every Step partial, move policies to node modifiers, and replace metadata constructor usage with generated properties.

<!-- section: behavior-contract -->
## Behavior contract

<!-- behavior-change: OB-01 -->
| ID | Observable boundary | Current | Expected | Preserved |
| --- | --- | --- | --- | --- |
| OB-01 | Per-node configuration | The workspace candidate already has `DependsOn` and fluent retry, timeout, and display modifiers, with Attribute policy fallback | Preserve those node APIs but remove `[StepPolicy]`; omitted policies are zero retries and infinite timeout | Node identity, ordering, cancellation, cleanup, and fixed compile-time values |
| OB-02 | Generated leaf context | DisplayName is constructor metadata and no Step logger is generated | Every valid leaf receives required `DisplayName`; `[StepLogger]` additionally receives required `ILogger` with a stable type-and-display category | Fresh Step per attempt and caller-owned logging services |
| OB-03 | Unified composition | A user-authored partial class inheriting `Pipeline` is the only graph definition | An attributed Composite ref struct plus exact `Configuration(StepGraph)` declares a root or reusable nested graph | Static topology, direct calls, typed inputs/results, and source-only configuration |
| OB-04 | Root DI facade | The generated executor class stores caller-provided `IServiceProvider` | A Composite exposes generated nested `Pipeline`; service-free Composite execution has no provider parameter, while a transitively service-requiring Composite accepts and propagates caller-owned `IServiceProvider` | Caller scope ownership and readiness-time service resolution |
| OB-05 | Nested execution | A graph cannot be registered as one Step | A Composite is one typed parent node whose result is its generated Results value; root and nested paths share semantics | Child policy isolation, failure propagation, draining, and cooperative cancellation |
| OB-06 | Compile-time safety | Leaf and Pipeline shapes are checked separately and partial context is absent | Invalid leaf/Composite shapes, graph cycles, modifier values, builder escape, member collisions, and unsafe ref-like inputs fail compilation | Symbol binding, warnings-as-errors, no guessed runtime fallback |
| OB-07 | Package and migration | Package/docs expose the old Pipeline, policy Attribute, and metadata API | A package consumer compiles only the new API with warnings as errors; graph-independent execution support is ordinary public Runtime compiler infrastructure rather than generated source; generated execution remains trimmable and migration is complete | Matching analyzer/runtime packaging |
| OB-08 | Runtime cost | Maintained benchmarks measure the class-based generated executor | Facade construction stays outside invocation work; composition introduces no runtime graph/reflection/boxing and meets AC-07 | Equivalent-work measurement and semantics-first interpretation |

<!-- acceptance-case: AC-01 -->
### AC-01 — Node policy and control ordering

Several registrations of one Step type may use different fixed retry, timeout, and display modifiers. `DependsOn` waits for successful completion without transporting a value. Duplicate or invalid modifiers fail compilation; no type-level policy fallback exists.

<!-- acceptance-case: AC-02 -->
### AC-02 — Generated DisplayName and Logger

Each fresh attempt receives the configured display name, otherwise the registration local name or `<StepType>#<index>`; a root Composite uses its type name. `[StepLogger]` resolves `ILoggerFactory` once when its node becomes ready, creates category `<fully-qualified Step type>[<DisplayName>]`, and reuses the logger across leaf retries. Missing logging services fail before the first attempt and are not retried. Unmarked Steps emit no logging dependency or scope.

<!-- acceptance-case: AC-03 -->
### AC-03 — Root and nested Composite are one graph definition

A valid Composite configuration produces strong input methods, a generated Results value, and a nested Pipeline facade. Registering that Composite in another `StepGraph` returns a typed node handle for the same Results and creates no facade object. Parent retry reruns the whole Composite; child policies remain local.

The accepted declaration matrix is:

| Declaration | Required source shape | Generated execution surface |
| --- | --- | --- |
| Leaf | top-level, non-file-local, non-generic `internal readonly ref partial struct`; exactly one of `IStep`, `IStep<TResult>`, `IAsyncStep`, or `IAsyncStep<TResult>`; one usable by-value constructor; no ref-like/pointer/function-pointer parameters or user required members | required DisplayName and optional Logger properties; direct leaf method remains user-authored |
| Composite | top-level, non-file-local, non-generic `internal` or `public readonly ref partial struct` marked `[CompositeStep]`; no leaf Step interface or user execution member; zero inputs or one primary-constructor input list; exactly one non-static, non-async, non-generic `void Configuration(StepGraph)` body; no service/ref-like/pointer/function-pointer input | generated sync or async Composite invocation contract, required context, Results, completion-only entry point, and nested `public sealed class Pipeline` |

Primary-constructor parameters are Composite data inputs, never DI inputs. A public Composite and its generated contract may be consumed from another compilation using the matching package; an internal Composite remains compilation-local. Reserved generated-member collisions fail compilation.

<!-- acceptance-case: AC-04 -->
### AC-04 — DI respects readiness and caller ownership

The generated Pipeline can be constructed by normal .NET DI. It stores and propagates the caller-owned provider only when the transitive graph needs services or logging; otherwise the generated Composite execution surface and core omit the provider entirely. It never creates/disposes a scope or passes the provider to user Step code. A node resolves its declared keyed or unkeyed services only after dependencies succeed and before its retry loop; leaf retries reuse that resolution while constructing a fresh Step each attempt. Retrying a whole Composite starts fresh child invocations.

<!-- acceptance-case: AC-05 -->
### AC-05 — Ref-struct async lifetime is valid

Generated ref-struct instance methods never await, capture `this`, or enter an async state machine. They copy supported inputs and generated context into a static execution core and immediately return its Task. Unsupported ref-like inputs fail at compile time. CS0282 is suppressed only for valid generated-context Steps; unrelated partial structs still report it.

<!-- acceptance-case: AC-06 -->
### AC-06 — Breaking migration is explicit

No public compatibility layer preserves the old Pipeline, policy Attribute, or metadata APIs. Runtime/analyzer packaging, README, architecture, examples, and diagnostics consistently describe the new model, and an external package consumer compiles representative root/nested/DI/logging code with warnings as errors and trimming enabled.

<!-- acceptance-case: AC-07 -->
### AC-07 — Performance remains proportionate

An equivalent same-job flat and one-boundary nested Composite benchmark covers synchronous, completed-Task, and yielding paths with facade construction in setup. It uses two launches, five warmups, fifteen measured iterations, and 250 ms target iteration time on an otherwise idle host. Synchronous nesting adds zero managed bytes. Completed or yielding nesting may add at most 512 managed bytes per invocation for generated Task/state-machine/cancellation state. Completed-Task nesting may add at most 16 ns to the flat mean because a relative ratio magnifies the fixed boundary cost at a sub-100 ns baseline. For synchronous and yielding paths, a nested/flat mean ratio above 1.15 blocks only when their reported 99.9% confidence intervals do not overlap. Existing Chain, Diamond, ValueSync, and ValueAsync comparisons are rerun with the same job and reported without cross-session causal claims.

## Constraints and risks

- Configuration is source-only and never invoked at runtime; all topology and symbol binding are compile-time concerns.
- Fully synchronous graphs stay synchronous. Default policies emit no retry loop or timeout source. Generated execution drains started work and preserves current cancellation, failure, disposal, and argument-evaluation semantics.
- Service resolution stays outside a leaf retry loop. Registering a provider-holding facade as singleton explicitly selects the root provider; the library does not create or repair application scopes.
- A Composite boundary may repeat completed child side effects when its parent node is retried; documentation must state this.
- Public Composite consumption across compilations must remain statically bound and directly invoked; it may not introduce interface dispatch or boxing.
- Generated public API, diagnostics, package dependencies, warning suppression, and migration are one compatibility surface.
- Escalation triggers: framework-owned scope, automatic NullLogger, dynamic graphs, runtime dispatch/reflection, exposing provider to user Steps, retaining Step instances, changing failure selection, or performance outside AC-07.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

None. ADR-0001 and ADR-0002 are accepted and pinned above.

<!-- section: delivery-brief -->
## Delivery brief

- Outcome: one releasable breaking candidate across Pipeline runtime, analyzer/generator, package, consumer-facing documentation, playground, focused tests, trimming proof, and maintained Pipeline benchmarks.
- Environment: .NET 10 SDK; bundled analyzer/runtime; Microsoft DI and logging abstractions; existing TUnit and BenchmarkDotNet projects.
- Likely touchpoints are non-binding. Internal emitter/model organization, diagnostic IDs, static-core factoring, and test-file layout remain implementation choices.
- Delivery remains single because leaf context, Composite discovery, factory generation, and execution all change the same symbol model and emitters; no intermediate public surface is releasable or supplies independent value, while package/docs/benchmarks verify that one cutover.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: AC-01 purpose=acceptance shape=component -->
<!-- primary-proof: AC-02 purpose=boundary shape=component -->
<!-- primary-proof: AC-03 purpose=acceptance shape=contract -->
<!-- primary-proof: AC-04 purpose=boundary shape=component -->
<!-- primary-proof: AC-05 purpose=structural shape=contract -->
<!-- primary-proof: AC-06 purpose=boundary shape=contract -->
<!-- primary-proof: AC-07 purpose=acceptance shape=benchmark -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| AC-01 | Primary | Per-node policy and control dependency behavior pass across sync/async execution | Pipeline Release test project |
| AC-02 | Primary | Generated context and real logging DI satisfy category, timing, retry, and absence behavior | Pipeline Release test project |
| AC-03 | Primary | Root and nested Composite public contracts compile and execute with typed results | Pipeline Release tests plus generated-source assertions |
| AC-04 | Primary | Scoped/keyed/transient services resolve at node readiness with no framework-owned scope | Pipeline Release test project |
| AC-05 | Primary | Warnings-as-errors consumers compile valid ref structs and reject unsafe declarations | Pipeline Release tests plus package consumer |
| AC-06 | Primary | Package, trimming, docs, and breaking surface agree | Release build, package consumer, trimming verifier, repository search |
| AC-07 | Primary | Correctness verifier and same-session benchmark satisfy the stated cost boundary | Benchmark `--verify`, focused Composite run, then maintained comparison filters |

The authoritative commands are:

```text
dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests/TedToolkit.Orchestration.Pipeline.Tests.csproj --configuration Release
dotnet build --configuration Release
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*CompositeSyncNestingBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-sync
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*CompositeNestingBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-async
dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release --no-build -- --filter '*ChainBenchmarks*' '*DiamondBenchmarks*' '*ValueSyncBenchmarks*' '*ValueAsyncBenchmarks*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250 --artifacts artifacts/composite-step-review-fix-maintained
```

## Candidate evidence — 2026-09-08

- Release solution build: succeeded with 0 warnings and 0 errors.
- Pipeline behavior/contract suite: 168 discovered, 168 passed, 0 failed, 0 skipped, including chained/fan-in `DependsOn` ordering, root/nested logger initialization, nested failure/cancellation draining, ordinary-constructor rejection, exact same-name factory binding across assemblies, and collision-resistant generated context hint names.
- Benchmark correctness verifier: all adapters plus flat/nested synchronous, completed-task, and yielding Composite paths passed.
- External packed-package consumer: warnings-as-errors compile, trimmed publish, metadata check, and execution succeeded; output was `42`.
- Playground: root DI facade and repeated Composite execution completed successfully.
- Earlier independent review blockers were corrected: every attributed Composite is type-analyzed; context emission plus CS0282 suppression share the full valid-contract predicate; root and nested Composite calls construct the generated context before execution; and graph binding now selects the exact generated factory symbol rather than the first matching simple name.
- Public factory collisions receive deterministic namespace-qualified generated class names, with an explicit static-call escape hatch for identical signatures. Composite declarations with ordinary explicit constructors are rejected instead of being mistaken for a zero-input primary constructor.
- Generated Step context hint names include a stable SHA-256 of the fully qualified metadata name, so otherwise-valid names such as `A_B.C` and `A.B_C` cannot collide through punctuation normalization.
- Removed-surface search and `git diff --check`: passed; remaining old API names are migration prose and negative absence assertions.
- Runtime-support extraction: graph-independent cancellation, observation, retry, and timeout code now lives in public IntelliSense-hidden `PipelineExecutionSupport`; generated fixed support and `EmptyServices` are absent, and service-free Composite signatures omit `IServiceProvider`.
- Focused Composite timings passed AC-07: synchronous flat/nested 5.235/6.264 ns and 0 B with overlapping 99.9% intervals; completed-task flat/nested 47.79/55.80 ns and 288/360 B; yielding flat/nested 1.793/1.863 μs and 440/560 B with overlapping intervals. BenchmarkDotNet's reported multimodality is recorded as an interpretation limitation.
- The same post-fix job completed all 24 maintained Chain, Diamond, ValueSync, and ValueAsync comparisons; exact reports and commands are recorded in the benchmark documentation.
- Independent candidate-bound implementation review concluded `Ready to merge` with no Blocking, Important, or Suggestion findings after reproducing the 70-path ordinal workspace digest `c1b5d8e95cea3776194587de9a49fd0fb2f8d4d2913e8c1e36bb9ecb47dd0d47`.
- Workspace binding hashes a manifest of each sorted path, tracked state, and per-file SHA-256 for every tracked change and untracked deliverable after normalizing only this record's self-referential `candidate-binding` marker to `none`; ignored build and benchmark artifacts are excluded.

<!-- section: completion-criteria -->
## Completion

The exact candidate satisfies AC-01 through AC-07, passes independent candidate-bound implementation review, records benchmark environment/results and limitations, and leaves product, architecture, migration, package, and examples consistent with the accepted ADRs. Active architecture records cite the accepted current ADR blobs rather than stale pre-decision pins. No operational handoff is required. After merge/reference release, the temporary change record has no retention exception.
