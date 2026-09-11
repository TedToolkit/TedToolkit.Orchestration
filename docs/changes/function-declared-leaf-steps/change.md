# Replace Step objects and implicit roots with attributed functions

> This path began as the Leaf-only migration record. It is intentionally retained as the single expanded migration for Leaf, Composite, and Pipeline so the existing workspace candidate has one authoritative handoff.

<!-- change-format: 3 -->
<!-- workflow-profile: controlled -->
<!-- change-kind: migration -->
<!-- change-status: completed -->
<!-- delivery-shape: single -->

- Priority: P1
<!-- approval-source: user explicitly approved the function Step/Configuration/Pipeline contract, two-phase hidden protocol, and hierarchical per-level DisplayPath logging and requested implementation in the current Codex task on 2026-09-10 -->
<!-- candidate-binding: workspace:d2cc400af48d521c29920b771bdb19c40e7c8442:sha256:c96ca773d141a11dbb642918dcdaab1422ffe8e36a0283824bbe3770d4fbc0ff -->

<!-- section: goal-rationale -->
## Goal and rationale

Let consumers declare every reusable Pipeline operation as a static function: `[Step]` marks a Leaf, `Configuration(StepGraph, ...)` describes a Composite Step, and `[Pipeline]` explicitly selects either function as a root facade. This removes Step objects and implicit root types while retaining generated, strongly typed execution.

<!-- section: scope -->
## Scope and non-goals

- In scope: the joint migration from the `HEAD` object-based Leaf/Composite API to function declarations; Configuration parameter binding; optional Pipeline generation and naming; generated direct/root/nested calls; DI, hierarchical per-level DisplayPath logging, cancellation and defaults; versioned cross-assembly public Composite protocol; diagnostics; migration of repository consumers, tests, packages and documentation.
- Workspace note: uncommitted static-Leaf edits are an implementation candidate within this change, not a delivered predecessor and not completion evidence.
- Non-goals: dynamic graphs, reflection or runtime dispatch; multiple Configuration methods per containing type; cross-assembly discovery of ordinary Leaf methods; ValueTask/custom awaitables; framework-owned DI scopes; new node policies or changed execution outcomes.
- Compatibility: this intentionally removes the former four Leaf interfaces, `[CompositeStep]`, `[StepLogger]`, Composite ref-struct declarations/context, `ICompositeStep<TResult>` and `IAsyncCompositeStep<TResult>`. Matching runtime/analyzer packages and consumer recompilation remain required.

<!-- section: behavior-contract -->
## Behavior contract

<!-- behavior-change: OB-01 -->
| ID | Observable boundary | Current at `HEAD` | Expected | Preserved |
| --- | --- | --- | --- | --- |
| OB-01 | Reusable Step declaration | Leaf and Composite are attributed ref structs with constructor data and instance execution/configuration methods | `[Step]` marks a static Leaf; a convention-matched static Configuration function compiles into a Composite Step | Static graph validation, typed registration and direct calls |
| OB-02 | Root Pipeline generation | Every Composite receives a nested `Pipeline`; a Leaf cannot be a root facade | `[Pipeline]` on a Leaf or Configuration generates one named nested facade; unmarked Steps generate none | Caller-owned DI and reusable facade behavior |
| OB-03 | Inputs and services | Step data inputs come from constructors; generated Composite Pipeline methods receive them | Function parameters define data/service/cancellation/default projection; all business inputs remain on execution methods | Parameter order/nullability, DI boundary and cancellation ownership |
| OB-04 | Composite metadata | Interfaces and generated instance context identify Composite execution across assemblies | Versioned generated static protocol identifies and invokes public Composite Steps without user interfaces or runtime discovery | Public cross-assembly nesting and typed Results |
| OB-05 | Nested non-generic Logger category | A Logger category contains only the current Step identity and node DisplayName | Each Step owns one path segment and the category contains its method identity plus the root-to-current DisplayPath | Logger creation count, resolution timing and generic Logger behavior |

<!-- acceptance-case: AC-01 -->
### AC-01 — Configuration functions compose locally and across assemblies

```gherkin
Scenario: Compile and consume a public Composite protocol
  Given a producer assembly with a public static partial container and public static void Configuration whose first parameter is StepGraph
  And its later parameters include ordinary, defaulted, FromServices, non-generic ILogger and trailing CancellationToken forms
  When the producer is emitted and a separate consumer is compiled using only its metadata reference plus matching runtime and analyzer
  Then the consumer can register the Composite by its containing-type factory name, bind typed Results and nest or execute it
  And its hidden prepare phase carries the accumulated root-to-current DisplayPath and resolves direct Configuration services once outside outer retries
  And each outer attempt invokes only the prepared execute phase
  And service, logger, default and cancellation behavior uses the version 1 static protocol without reflection or interfaces
```

<!-- acceptance-case: AC-02 -->
### AC-02 — Pipeline is an explicit, stable function entry

```gherkin
Scenario: Generate a root facade for either Step kind
  Given a valid Step or Configuration function in a partial static owner
  When it is marked Pipeline with no Name or with the valid stem Import
  Then a public-or-internal sealed <method>Pipeline or ImportPipeline nested type is generated
  And an unmarked function receives no facade
  And synchronous roots expose Execute and ExecuteWithoutResults while asynchronous roots expose their Async forms
```

<!-- acceptance-case: AC-03 -->
### AC-03 — Execution receives business inputs while DI stays in construction

```gherkin
Scenario: Execute a generated Pipeline
  Given an entry function with ordinary, nullable, defaulted, FromServices, keyed-service, non-generic ILogger and cancellation parameters
  When its generated Pipeline is constructed and executed
  Then its public constructor is parameterless or receives only IServiceProvider according to direct/transitive service need
  And Execute receives data with original names, types, nullability, order and defaults plus a trailing optional execution token
  And StepGraph, FromServices parameters and business data are never constructor-bound
  And each WithDisplayName replaces only its current Step segment
  And non-generic ILogger uses method identity plus the slash-joined root-to-current DisplayPath once outside retries
```

<!-- acceptance-case: AC-04 -->
### AC-04 — Obsolete object contracts are absent

```gherkin
Scenario: Inspect the matching runtime, analyzer and generated public surface
  Given the function-declared Step release
  When package assemblies and generated sources are inspected
  Then all four Leaf interfaces, CompositeStepAttribute, StepLoggerAttribute, both Composite interfaces, generated instance context and the obsolete suppressor are absent
  And only the EditorBrowsable-hidden versioned Composite compiler protocol remains as new public infrastructure
```

<!-- acceptance-case: AC-05 -->
### AC-05 — Invalid declarations, names and collisions fail statically

```gherkin
Scenario: Compile an ambiguous function declaration
  Given an invalid Configuration shape, direct Configuration invocation, unsupported protocol version, invalid Pipeline Name stem, non-partial Pipeline owner, reserved member collision, duplicate final Pipeline type name or ambiguous unqualified factory call
  When the analyzer processes local and referenced candidates
  Then it reports an actionable diagnostic on every invalid or conflicting declaration
  And generated provider, display-name, state or token parameter names deterministically append underscores when user data uses the same names
  And it emits no guessed, numbered, hashed, interface or reflection fallback API
```

<!-- acceptance-case: AC-06 -->
### AC-06 — Existing execution behavior survives the migration

```gherkin
Scenario: Run migrated representative graphs
  Given the current sequential, parallel, nested, DI, Logger, retry, timeout, cancellation, cleanup, failure and result scenarios expressed as functions
  When generated Steps and explicit Pipelines execute
  Then their observable values, ordering, resolution timing, cancellation, cleanup and exceptions remain unchanged
  And only the approved non-generic Logger category changes from one node name to its root-to-current DisplayPath
```

## Constraints and risks

- Governing decisions: [ADR-0003](../../adr/ADR-0003-function-declared-leaf-steps.md) remains authoritative for Leaf return/parameter/Logger rules; [ADR-0004](../../adr/ADR-0004-function-declared-composite-and-pipeline.md) replaces object Composite/root decisions and normatively defines Configuration, Pipeline and protocol version 1.
- Configuration is an internal/public static void method in a top-level internal/public static partial class. Its first and only StepGraph parameter is required; a unique trailing CancellationToken is optional; later parameters otherwise use Leaf classification.
- The Composite graph factory is named for the containing type. Its data parameters and generated root methods preserve names, types, nullability and explicit defaults; direct Configuration services are omitted and prepared once outside any outer retry. Generated provider/display-path/state/token parameter names append underscores when needed to avoid user data names.
- Default Pipeline name always appends `Pipeline` to the method name; `Name` supplies only a valid stem. Explicit stems ending in `Pipeline` and final member collisions are diagnostics. Same-signature factories from distinct namespaces/assemblies remain selectable through their generated extension types; referenced ordinary Leaf discovery remains out of scope.
- Public cross-assembly Composite composition uses only the protocol-v1 hidden state, `__TedToolkitPrepareCompositeStep`, and attributed `__TedToolkitExecuteCompositeStep` defined by ADR-0004. Prepare preserves direct service/Logger timing and carries the accumulated DisplayPath; Execute is the only per-attempt call.
- Pipeline constructors store only a caller-owned provider when required; they do not own scopes or business inputs.
- Migration is an intentional source/binary break with no compatibility layer. Recovery is source migration with the previous matching runtime/analyzer package still available through version control/package history.
- Escalation triggers: multiple Configuration methods per container, top-level Pipeline types, cross-assembly ordinary Leaf discovery, new return forms, protocol version/shape changes, dynamic/reflection fallback, provider/scope ownership changes, or execution-semantic changes.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

None. The full Leaf/Composite/Pipeline migration begins from the exact approved `HEAD` baseline; existing workspace edits are candidate implementation only.

<!-- section: delivery-brief -->
## Delivery brief

- Outcome and target delivery area: one matching runtime/analyzer release with function-declared Leaf/Composite Steps, opt-in generated Pipeline facades, and protocol-v1 public Composite composition.
- Other real start conditions or resource prerequisites: .NET SDK, current TUnit generator harness, package compatibility script, and exact workspace baseline/binding.
- Likely touchpoints (non-binding): Pipeline attributes/contracts; Step/Configuration symbol models; Composite graph and execution emitters; protocol marker/discovery; diagnostics/suppressor; tests, package consumer, playground, benchmarks and Pipeline documentation.
- Private implementation choices left open: incremental-provider decomposition, private helper names, emitter factoring, diagnostic IDs/messages, and test file organization. The public generated API and cross-assembly protocol are not private choices.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: AC-01 purpose=boundary shape=contract -->
<!-- primary-proof: AC-02 purpose=acceptance shape=contract -->
<!-- primary-proof: AC-03 purpose=boundary shape=component -->
<!-- primary-proof: AC-04 purpose=boundary shape=contract -->
<!-- primary-proof: AC-05 purpose=acceptance shape=contract -->
<!-- primary-proof: AC-06 purpose=regression shape=component -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| AC-01 | Primary | A producer is emitted, then a separate compilation using only its metadata reference discovers protocol v1, prepares once, executes per attempt, preserves typed Results/defaults/DI/logger/cancellation, and selects a same-name but distinct-signature candidate exactly | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests --configuration Release`; bounded test lives in `CompositeStepTests` and must explicitly emit producer then consumer |
| AC-02 | Primary | `[Pipeline]` generates exactly the requested Leaf/Composite facades, visibility, method set and stable names; unmarked functions generate none | Run the complete Pipeline test project |
| AC-03 | Primary | Constructors contain only required DI, execution methods expose projected data/default/cancellation inputs, and nested Logger assertions observe paths such as `Root/Import/Save` with unchanged creation timing | Run the complete Pipeline test project |
| AC-04 | Primary | Runtime reflection/package API and generated-source assertions find no obsolete object contracts/context and do find only the hidden protocol infrastructure | Run the complete Pipeline test project and package compatibility script |
| AC-05 | Primary | Invalid shapes, protocol, stems, owners, reserved members and local/reference collisions report diagnostics without fallback | Run the complete Pipeline test project |
| AC-06 | Primary | Migrated execution regressions preserve orchestration outcomes and timing | Run the complete Pipeline test project |
| Public package boundary | Conditional | Matching packages build warnings-as-errors consumers across supported frameworks and trimming | `pwsh -NoProfile -File tests/verify-package-compatibility.ps1` |
| Repository integration | Conditional | Runtime, analyzer and unaffected StateMachine projects compile together | `dotnet build TedToolkit.Orchestration.slnx -c Release` |
| Pipeline playground | Conditional | Runnable consumer example executes through explicit function Pipeline | `dotnet run --project playground/TedToolkit.Orchestration.Pipeline.Playground --configuration Release` |
| Pipeline benchmark adapter | Conditional | Benchmark correctness adapter executes without collecting measurements | `dotnet run --project benchmarks/TedToolkit.Orchestration.Pipeline.Benchmarks --configuration Release -- --verify` |

<!-- section: completion-criteria -->
## Completion

Complete when repository consumers use function-declared Composite Steps and explicit Pipeline entries; obsolete object contracts are absent; emitted-producer/metadata-only-consumer composition, package/trimming, Release build, playground, benchmark adapter and all execution regressions pass; durable Pipeline architecture/principles describe the delivered model; and the exact candidate passes independent implementation review. Cleanup remains gated by terminal completion, durable extraction, authoritative-reference checks and explicit continuation.

## Implementation evidence

- Candidate artifacts: Pipeline runtime attributes/compiler protocol; analyzer symbols, graph reader, emitters, diagnostics and incremental generator; migrated tests, package consumer, playground, benchmark adapters, README, architecture, principles, ADRs, and this change record.
- Primary/regression proof: `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests --configuration Release -- --maximum-failed-tests 10` passed 201/201 with no failures or skips. The suite loads separate producer/consumer assemblies and observes protocol-v1 defaults, ordinary/keyed/dynamic DI, one prepare across two outer attempts, per-attempt execute, hierarchical Logger category and caller cancellation. It also proves local and metadata-only direct-Configuration diagnostics, multiple special Logger parameters sharing one per-Step category Logger, cross-namespace omitted enum defaults, root Leaf ordinary/keyed service resolution before special Logger creation and failure precedence, Pipeline kind/name/visibility/method surfaces, deterministic generated-name collision handling, exact selection of identical metadata-qualified Composites through distinct assembly aliases, actionable ambiguous-unqualified calls, all four local reserved protocol-member collisions without generated fallback, and representative malformed/duplicate/unsupported protocol `TTP019` branches.
- Repository proof: `dotnet build TedToolkit.Orchestration.slnx --configuration Release --no-restore` passed with 0 warnings and 0 errors across all configured target frameworks.
- Conditional proof: Pipeline playground rebuilt without warnings and completed all three executions; benchmark correctness adapter passed all 24 direct/generated/nested checks; `tests/verify-package-compatibility.ps1` built both packages, compiled consumers for all supported frameworks, and completed trimmed win-x64 execution with `42:Completed`.
- Migration/documentation: repository consumers now use function declarations and explicit Pipeline facades; durable architecture and README describe per-level display paths and no Step context; `docs/migrations/function-declared-steps.md` maps removed Leaf/Composite objects, interfaces, Logger/context access and root construction to the new API. Recovery remains a source/package rollback to the prior matching runtime and analyzer.
- Scope deviations: None. Same-name factories preserve the pre-existing exact generated-extension selection path; only an ambiguous unqualified call is rejected.
- Remaining risk: the public hidden protocol is versioned but intentionally binary-breaking for mismatched runtime/analyzer releases; matching packages and consumer recompilation remain required.

## Previous independent implementation review

- Result: Not ready against `workspace:d2cc400af48d521c29920b771bdb19c40e7c8442:sha256:798375e537af3921c4c50d89585b692d41b61ec5b981e63e3b0ae87d636f7e77`.
- Required implementation correction: referenced Composite discovery must diagnose unsupported protocol versions, duplicates, and malformed static/accessibility/state/prepare/execute shapes with `TTP019` instead of silently omitting them or leaking ordinary compiler errors.
- Required proof correction: add metadata-only cross-assembly execution covering defaults, DI/logger/cancellation and prepare-once/per-attempt behavior; add the Pipeline surface/name/visibility/collision matrix and `TTP018`/`TTP019` rejection cases promised by AC-01, AC-02 and AC-05.
- Required documentation correction: add durable consumer migration guidance for the removed object contracts outside `docs/changes/`.
- Reverification: after correction and rebinding, rerun the complete Pipeline suite, Release solution build, package compatibility/trimming, Pipeline playground and benchmark correctness adapter before independent re-review.
- Correction result: protocol discovery now detects all visible protocol evidence before version filtering and rejects unsupported versions, duplicate markers, incomplete/reserved members, invalid owner/state/Results/prepare/execute accessibility and signatures with `TTP019`; no invalid factory is emitted. All requested proof and documentation corrections above are present and the full reverification gate is Green on the new workspace candidate.

## Independent implementation re-review

- Result: Not ready against `workspace:d2cc400af48d521c29920b771bdb19c40e7c8442:sha256:c059e36d3487a4c577c7742a91a28c3d44851c8a426f90c87c7e9b7a24bbb8c3`; independent binding checks before and after review matched all 67 candidate entries.
- Required implementation correction: allocate generated facade locals and Composite protocol provider/display-path/state/token/result names from one per-method used-name set so every approved reserved-name and underscore-chain collision compiles deterministically.
- Required implementation or contract correction: preserve exact referenced assembly identity when two public Composites have the same metadata-qualified type name, factory signature and distinct assemblies; otherwise narrow and reapprove the public selection contract. No contract narrowing is currently approved.
- Required proof correction: add executable reserved-name collision cases, identical-FQN metadata-only producer selection, explicit ambiguous-unqualified factory diagnostics, and representative `TTP019` cases for missing/wrong Results, prepare return/provider, execute state/return, and marker placement.
- Important documentation/build correction: reconcile ADR-0004's same-signature collision sentence with the approved exact-selection rule, and prevent generated hidden protocol members from causing consumer `CS1591` documentation warnings.
- Candidate-bound verification result: Pipeline tests passed 185/185 with no skips; Release solution build passed with 0 warnings/errors; package multi-target/trimming passed with `42:Completed`; playground completed all executions but exposed four `CS1591` warnings, including three generated protocol members; benchmark correctness reported 24 PASS results.
- Correction result: facade and Composite protocol identifiers now use one deterministic allocator per generated method; exact assembly identity and distinct `extern alias` values survive Composite discovery, rebinding, emitted types and calls; unaliased identical metadata names and ambiguous unqualified calls report actionable diagnostics. The promised collision, identical-FQN, ambiguity and malformed-protocol proof is executable, ADR-0004 matches the exact-selection contract, and generated hidden protocol members carry XML documentation. Reverification passed 190/190 tests, a zero-warning Release solution build, package multi-target/trimming with `42:Completed`, a zero-warning Playground run, and all 24 benchmark correctness checks.

## Independent implementation review after collision corrections

- Result: Not ready against `workspace:d2cc400af48d521c29920b771bdb19c40e7c8442:sha256:e4d44fe35f69a6563f588cb6d3c113ad575525ec4bd20775f5e17ed36661abf0`; independent binding checks before and after review matched all 68 candidate entries.
- Code conclusion: no implementation-correctness blocker, design deviation, or missing durable documentation was found across AC-01 through AC-06.
- Required proof correction: AC-05 needs a positive valid-Composite direct-invocation case asserting error-level `TTP015`, plus local Composite owner cases for `Results`, `__TedToolkitCompositeStepState`, `__TedToolkitPrepareCompositeStep`, and `__TedToolkitExecuteCompositeStep` asserting `TTP009` and no generated protocol/facade fallback.
- Candidate-bound verification result: Pipeline tests passed 190/190 with no failures or skips; Release solution build passed with 0 warnings/errors; package multi-target/trimming passed with `42:Completed`; Playground completed all executions with 0 warnings; benchmark correctness passed 24/24; acceptance validation and before/after workspace binding checks passed.
- Next route: return to `implement-change` for the two missing AC-05 proof partitions, rebind the corrected candidate, rerun the full conditional gate set, and independently re-review the affected test lane before advancing lifecycle status.
- Correction result: a positive valid-Composite direct invocation now asserts error-level `TTP015`, its actionable message and exact invocation location; parameterized local-owner cases cover all four reserved protocol member names, require error-level `TTP009`, and assert that neither protocol nor Pipeline fallback is generated. No production artifact changed for this correction. Reverification passed 195/195 tests, a zero-warning Release solution build, package multi-target/trimming with `42:Completed`, a zero-warning Playground run, and all 24 benchmark correctness checks.

## Independent implementation review after proof completion

- Result: Not ready against `workspace:d2cc400af48d521c29920b771bdb19c40e7c8442:sha256:19c0515d7fa16c08bfc5d6787f50860d91cb13da7c678e584dd7f7f1d1282635`; independent binding checks before and after review matched all 68 candidate entries.
- Required implementation correction: omitted local Step/Composite defaults must be rendered from their semantic constant and fully qualified type rather than replaying declaration-site source syntax in a different generated scope. A bounded valid enum-default probe reproduced `CS0103` across namespaces.
- Required execution correction: a root Leaf Pipeline must resolve ordinary/keyed services before resolving `ILoggerFactory` and creating the special non-generic Logger, while preserving declared invocation argument order. A bounded probe observed the current invalid order `logger-factory,logger,marker,call`.
- Required proof correction: add a cross-scope omitted enum or aliased-constant default test with an observable runtime value, plus a root Leaf Pipeline test covering ordinary/keyed services and the special Logger with resolution order, creation count, setup-failure precedence, and proof that the function is not entered on setup failure.
- Candidate-bound verification result: readiness validation passed; Pipeline tests passed 195/195 with no failures or skips; Release solution build passed with 0 warnings/errors; package multi-target/trimming passed with `42:Completed`; Playground completed all executions; benchmark correctness passed 24/24; both bounded defect probes reproduced on the unchanged candidate.
- Next route: return to `implement-change` for only these two defects and their focused regression proof, then rebind, rerun all required gates, and request independent re-review before advancing lifecycle status.
- Correction result: omitted defaults are now emitted from semantic constants with a factory-aware fully qualified type name, so declaration-site aliases or namespace imports are not replayed into another generated scope. Root Leaf facades resolve every ordinary/keyed service before creating the special non-generic Logger, then assemble invocation arguments in declaration order. Both reproductions were Red before the correction and Green afterward; full reverification passed 197/197 tests, a zero-warning Release solution build, package multi-target/trimming with `42:Completed`, all three Playground executions, and all 24 benchmark correctness checks.

## Independent final candidate review

- Result: Not ready against `workspace:d2cc400af48d521c29920b771bdb19c40e7c8442:sha256:b30d3c1203a3c91dd6a2921a78893faf0823870bc0fd08a5252b8a0e05287fb6`; independent before/after checks matched the baseline, all 68 workspace entries, the candidate digest, and the excluded lifecycle record.
- Required analyzer correction: a direct invocation of a public metadata-referenced `Configuration` must report error-level `TTP015`; source-only Configuration recognition currently misses this cross-assembly call.
- Required Logger correction: every accepted unkeyed non-generic `ILogger` parameter must bind to the one category Logger for that Step. A Configuration with two such parameters currently causes generator `CS8785` with `KeyNotFoundException`; Leaf classification is likewise first-parameter-only.
- Required dynamic-service correction: root Leaf and Composite DI lookup must normalize `dynamic` to runtime `object`, as the nested Leaf path already does, while retaining the declared invocation type. Current generated `typeof(dynamic)` produces `CS1962`.
- Required documentation correction: update `FromServicesAttribute` XML documentation from obsolete “step constructor parameter” wording to function-parameter terminology.
- Required proof correction: add a metadata-only producer/consumer direct-Configuration invocation test for `TTP015`; multiple-special-Logger Leaf and Configuration execution tests; and root-Leaf/direct-Configuration dynamic-service compile/execution coverage.
- Candidate-bound verification result: acceptance readiness passed; Pipeline tests passed 197/197 with no failures or skips; Release solution build passed with 0 warnings/errors; package multi-target/trimming passed with `42:Completed`; Playground completed all executions; benchmark correctness passed 24/24; `git diff --check` passed. Three bounded compiler/analyzer probes independently reproduced the defects on the unchanged candidate.
- Next route: return to `implement-change` for only these three implementation defects, the stale XML summary, and focused regressions; then rebind, rerun all required gates, and request independent re-review before advancing lifecycle status.
- Correction result: metadata-referenced Configuration calls now use validated protocol identity and report `TTP015`; every accepted unkeyed non-generic Logger parameter maps to the one category Logger created for its Step; all DI emitters normalize dynamic lookup to runtime object while retaining the declared invocation type; and `FromServicesAttribute` documentation now describes function parameters. Four focused reproductions were Red before correction and Green afterward. Full reverification passed 201/201 tests, a zero-warning Release solution build, package multi-target/trimming with `42:Completed`, all three Playground executions, all 24 benchmark correctness checks, and `git diff --check`.

## Independent implementation review after final corrections

- Result: Ready with follow-ups against `workspace:d2cc400af48d521c29920b771bdb19c40e7c8442:sha256:c96ca773d141a11dbb642918dcdaab1422ffe8e36a0283824bbe3770d4fbc0ff`; independent before/after checks matched the baseline, all 69 workspace entries, the candidate digest, and the separately hashed lifecycle record.
- Code conclusion: pass. AC-01 through AC-06 conform to the approved public API, protocol and execution contracts with no implementation blocker or design deviation.
- Test conclusion: all material partitions are adequate except the non-blocking AC-02 default-name edge `RunPipeline` to `RunPipelinePipeline`, which is correctly implemented but lacks a dedicated regression oracle.
- Candidate-bound verification: acceptance readiness passed; Pipeline tests passed 201/201 with no failures or skips; Release solution build passed with 0 warnings/errors; package multi-target/trimming passed with `42:Completed`; Playground completed all three executions; benchmark correctness passed 24/24; and `git diff --check` passed.
- Follow-up: optionally add an uncustomized `[Step, Pipeline]` method named `RunPipeline` and assert the generated `RunPipelinePipeline` facade. This Important finding is non-blocking and does not require another review unless the bound candidate is changed.
- Documentation disposition: ADR-0003, ADR-0004, the architecture guides, README and migration guide already capture the enduring contract; no further durable extraction is required before closure.

## Closure

- Exact candidate: `workspace:d2cc400af48d521c29920b771bdb19c40e7c8442:sha256:c96ca773d141a11dbb642918dcdaab1422ffe8e36a0283824bbe3770d4fbc0ff` remained unchanged through independent review and closure checks.
- Required review: independent implementation review passed as Ready with follow-ups; the remaining naming-boundary test suggestion is non-blocking and does not alter the delivered contract.
- Proof and handoffs: all primary and conditional gates recorded above passed on the bound candidate. No release, deployment, permission, production configuration, or other operational handoff is part of this repository delivery.
- Durable extraction: captured in ADR-0003, ADR-0004, the Pipeline architecture guides, README, and `docs/migrations/function-declared-steps.md`; no additional extraction is needed.
- Completion: all approved completion criteria are satisfied. Cleanup remains a separate, eligibility-checked action after the terminal record is present on the authoritative default-branch reference.
