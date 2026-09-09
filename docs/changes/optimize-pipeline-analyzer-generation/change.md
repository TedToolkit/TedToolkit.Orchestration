# Optimize Pipeline analyzer generation without behavior change

<!-- change-format: 3 -->
<!-- workflow-profile: controlled -->
<!-- change-kind: behavior-change -->
<!-- change-status: completed -->
<!-- delivery-shape: single -->

- Priority: P1
<!-- approval-source: user approved the independently reviewed contract and explicitly continued on 2026-09-09 -->
<!-- candidate-binding: workspace:00e3fa10a164e02de6048e7f05c7906b60a63885:sha256:bdb24f1d96a300db6cb6ee66e31cc2be6cdf81835b1fb18c2cc675553c231507 -->

<!-- section: goal-rationale -->
## Goal and rationale

Reduce Pipeline analyzer regeneration and temporary compilation work so consumer edits cause only the necessary generator work, while preserving every generated and runtime contract. The current generator combines the full Compilation with a collected configuration set, emits Step context sources twice, and reparses generated declarations to bind Composite graphs.

<!-- section: scope -->
## Scope and non-goals

- In scope: incremental provider granularity, elimination of duplicate Step-context discovery/emission, and removal or narrowing of temporary parsing/rebinding stages when their semantic dependency can be eliminated.
- Non-goals: changing supported declarations, diagnostics, generated public APIs or hint names, execution planning, runtime performance, cancellation/retry/timeout/DI semantics, packaging, or adding RoslynHelper control-flow APIs.
- Compatibility: valid and invalid consumer programs retain the same generated sources by meaning, diagnostic IDs/severity/messages, execution behavior, runtime/analyzer package boundary, and C# language-version support.

<!-- section: behavior-contract -->
## Behavior contract

<!-- behavior-change: OB-01 -->
| ID | Observable boundary | Current | Expected | Preserved |
| --- | --- | --- | --- | --- |
| OB-01 | Incremental generator execution after an unrelated consumer syntax edit | Full Compilation and collected configurations can rerun unchanged Step-context discovery and Composite generation | Unchanged per-Step context and Composite outputs are cached, and context discovery runs through one provider path | Generated source meaning, hint names, diagnostics, language support, and runtime behavior |

<!-- acceptance-case: AC-01 -->
### AC-01 — Unrelated edits reuse unchanged Step context output

```gherkin
Scenario: An unrelated syntax tree changes
  Given a compilation containing multiple valid Pipeline Steps and a Composite graph
  When the incremental generator reruns after changing a source tree unrelated to one Step
  Then the tracked output for that unchanged Step context is cached and each generated hint name remains unique
```

<!-- acceptance-case: AC-02 -->
### AC-02 — Unrelated edits reuse unchanged Composite output

```gherkin
Scenario: An unrelated syntax tree changes
  Given a compilation containing more than one valid Composite graph
  When the incremental generator reruns after changing a source tree unrelated to one Composite
  Then the tracked output for that unchanged Composite is cached
```

<!-- acceptance-case: AC-03 -->
### AC-03 — Existing Pipeline consumer behavior remains compatible

```gherkin
Scenario: Existing supported and rejected declarations are rebuilt
  Given the repository Pipeline generation, diagnostics, execution, DI, cancellation, policy, and results cases
  When they run against the optimized analyzer
  Then their generated APIs, diagnostics, and runtime observations remain unchanged
```

## Constraints and risks

- Preserve the analyzer-first compile-time control plane and generated runtime data plane in `docs/architecture/pipeline-system.md`.
- Do not introduce any new name-only or display-string symbol recognition. Existing display-string identity checks retain their current observable results in this delivery and any correction is a separately scoped change under the symbol-identity principle in `docs/principles/README.md`.
- The analyzer remains `netstandard2.0`, contains no shared mutable compilation state, and ships with the matching runtime package.
- Internal temporary compilations may be removed only where graph binding and diagnostics remain fully determined; otherwise narrow and document the remaining dependency instead of guessing.
- Source inspection must find one Step-context discovery provider path. Every remaining temporary `ParseText`, `AddSyntaxTrees`, or factory `Rebind` stage must name the generated semantic dependency that requires it; an unexplained stage blocks completion.
- Any changed diagnostic, generated signature/name, supported declaration, runtime behavior, public dependency, packaging surface, or RoslynHelper version is an escalation trigger requiring renewed approval.
- Recovery is a normal Git revert because the change has no migration or external operational state.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

None. Ready from the approved `00e3fa1` Orchestration baseline.

<!-- section: delivery-brief -->
## Delivery brief

- Outcome and target delivery area: a more incremental Pipeline analyzer generation pipeline with unchanged consumer output and behavior.
- Other real start conditions: existing Pipeline generator/component tests and architecture records remain authoritative.
- Likely touchpoints (non-binding): `PipelineExecutorGenerator`, `CompositeStepGenerator`, `StepContextEmitter`, generator test helpers, and Pipeline tests.
- Private implementation choices left open: provider/model types, cache representation, staging of temporary compilations, file split, and test organization.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: AC-01 purpose=acceptance shape=component -->
<!-- primary-proof: AC-02 purpose=acceptance shape=component -->
<!-- primary-proof: AC-03 purpose=acceptance shape=component -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| AC-01 | Primary | A tracked second generator run reports the unchanged Step-context output as cached and emits no duplicate hint names | Run the focused incremental-caching test in `TedToolkit.Orchestration.Pipeline.Tests` |
| AC-02 | Primary | A tracked second generator run reports the unchanged Composite output as cached after an unrelated edit | Run the focused incremental-caching test in `TedToolkit.Orchestration.Pipeline.Tests` |
| AC-03 | Primary | Existing Pipeline generation, diagnostics, execution, DI, cancellation, policy, and results tests pass unchanged | `dotnet run --project tests/TedToolkit.Orchestration.Pipeline.Tests -c Release` |
| Analyzer structure | Conditional | Step-context discovery has one path and each retained temporary binding stage states its dependency | `rg -n "StepContextEmitter.Emit|ParseText|AddSyntaxTrees|Rebind" src/TedToolkit.Orchestration.Pipeline.Analyzer -S` followed by bounded inspection of every match |
| Packaging and trimming | Conditional | The Release solution builds and the trimmed Pipeline package-consumer project remains publishable | `dotnet build TedToolkit.Orchestration.slnx -c Release --no-restore` and `pwsh -File tests/TedToolkit.Orchestration.Pipeline.Tests/verify-trimming.ps1 -Configuration Release` |

## Candidate result

- AC-01 and AC-02: UnrelatedEditsCacheStepContextsAndCompositeSources observes cached tracked outputs after an unrelated syntax-tree replacement; the Pipeline suite passed 173/173 on .NET 10.
- AC-03: the same 173-test component suite passed with no generated API, diagnostic, execution, DI, cancellation, policy, or result regression observed.
- Structure: Step-context discovery has one syntax-provider path. The retained context, Results-stub, and extension parsing stages each state the generated semantic dependency requiring it.
- Conditional gates: the Release solution built with 0 warnings and 0 errors; the trimmed package consumer published and returned 42.
- Changed delivery artifacts: Pipeline generator orchestration, Step-context discovery, Composite result publication, diagnostic collection seam, generated-source value model, and focused incremental test.
- Scope deviation: none. RoslynHelper 2026.9.9 is available, but dependency upgrade and symbol-identity behavior remain outside this candidate.
- Durable documentation: no architecture update is required because compile-time/runtime boundaries and consumer contracts are unchanged.

<!-- section: completion-criteria -->
## Completion

Complete when AC-01 through AC-03 pass on the exact candidate; the bounded analyzer-structure inspection confirms the single context-discovery path and justified remaining temporary stages; Release build and trimming verification succeed; and implementation review finds no unintended generated/public/runtime contract change. Any genuinely enduring architecture change must first be reflected in `docs/architecture/pipeline-system.md`. The temporary change record has no retention exception and is eligible for normal lifecycle cleanup after completion and reference release.
