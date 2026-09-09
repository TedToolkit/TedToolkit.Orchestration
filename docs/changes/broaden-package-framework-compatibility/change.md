# Broaden runtime package framework compatibility

<!-- change-format: 3 -->
<!-- workflow-profile: controlled -->
<!-- change-kind: behavior-change -->
<!-- change-status: completed -->
<!-- delivery-shape: single -->

- Priority: P1
<!-- approval-source: user approved the reviewed contract and requested continuation on 2026-09-09 -->
<!-- candidate-binding: workspace:7b192aacb8edab70f3c94bc5692806f66ec70303:sha256:65a47517d97b1965b77f9a3d02aea5703065e2ed01db1b435cac2be8210af410 -->

<!-- section: goal-rationale -->
## Goal and rationale

Allow Pipeline and StateMachine consumers from .NET Standard 2.0 through .NET 10 to compile against exact target-framework assets in the shipped NuGet packages while preserving generated behavior. Both runtime projects currently ship only `net10.0`, which excludes otherwise compatible consumers and contradicts the approved compatibility direction.

<!-- section: scope -->
## Scope and non-goals

- In scope: runtime package target frameworks, TFM-specific compiler-support code and compiler-feature metadata, analyzer asset packaging, consumer compatibility proof, and compatibility statements in durable documentation.
- Non-goals: multi-targeting analyzers, tests, playgrounds, benchmarks, or build tooling; changing Pipeline or StateMachine semantics; publishing packages; or completing package-specific onboarding documentation.
- Compatibility or deliberately preserved behavior: package IDs, analyzer/runtime version coupling, diagnostics, generated public APIs, cancellation/failure semantics, trimming behavior on supported modern targets, and inclusion of each analyzer's full runtime dependency closure remain unchanged. Pipeline exposes public, IntelliSense-hidden `System.Runtime.CompilerServices.IsExternalInit` only in `netstandard2.0`, `netstandard2.1`, `net472`, and `net48`; it exposes public, IntelliSense-hidden `RequiredMemberAttribute` and `CompilerFeatureRequiredAttribute` in those four assets plus `net6.0`. These types are absent from Pipeline `net7.0` through `net10.0` and from every StateMachine asset.

<!-- section: behavior-contract -->
## Behavior contract

<!-- behavior-change: OB-01 -->
| ID | Observable boundary | Current | Expected | Preserved |
| --- | --- | --- | --- | --- |
| OB-01 | A consumer restores and compiles either local NuGet package for a supported target | Only `net10.0` receives a compatible runtime asset | `netstandard2.0`, `netstandard2.1`, `net472`, `net48`, and `net6.0` through `net10.0` each compile using the package asset with the same TFM | Matching analyzers load from the package, generated public APIs compile unchanged, and existing runtime semantics remain unchanged |

<!-- acceptance-case: AC-01 -->
### AC-01 — Supported consumers compile from packed artifacts

```gherkin
Scenario: Supported consumers compile from packed artifacts
  Given locally packed Pipeline and StateMachine packages
  When a representative consumer restores and builds for every supported target
  Then NuGet selects a compatible runtime asset and both bundled generators produce compiling code
```

## Constraints and risks

- Import the repository's `AlmostAllFrameworks.props` target set so each declared consumer TFM resolves the exact matching runtime asset.
- Put public, IntelliSense-hidden `IsExternalInit` only in Pipeline `netstandard2.0`, `netstandard2.1`, `net472`, and `net48`; put public, IntelliSense-hidden `RequiredMemberAttribute` and `CompilerFeatureRequiredAttribute` in those assets plus `net6.0`. Do not add them to Pipeline `net7.0` through `net10.0`, any StateMachine asset, or generated consumer source.
- Keep both analyzers on `netstandard2.0` and package Pipeline analyzer dependencies beside its analyzer DLL under `analyzers/dotnet/cs`.
- Hide cross-TFM runtime API differences behind the existing IntelliSense-hidden compiler-support boundary when generated consumer code needs them.
- Preserve trimming for both packages on the existing .NET 10 package-consumer boundary.
- A required generated semantic change, compiler-feature type outside the exact legacy assets that need it, removal of an analyzer runtime dependency, inability to compile a declared consumer target, or a trimming regression is an escalation trigger.
- Rollback is the project, helper, generator, proof, and documentation diff; no persisted data or external state is migrated.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

None. Ready from the approved baseline.

<!-- section: delivery-brief -->
## Delivery brief

- Outcome and target delivery area: multi-target the two runtime packages and verify their package/analyzer boundary across the supported consumer matrix.
- Other real start conditions or resource prerequisites: .NET 10 SDK, installed/restorable target packs, and the existing local-package consumer pattern.
- Likely touchpoints (non-binding): both runtime project files, Pipeline compiler support and emitter, a StateMachine compatibility shim, analyzer asset props, the unified package-consumer verification, and compatibility documentation.
- Private implementation choices left open: helper names and signatures, test fixture layout, exact MSBuild item names, and compact project-file formatting.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: AC-01 purpose=acceptance shape=integration -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| AC-01 | Primary | A package-only consumer builds Pipeline and StateMachine generated code for `netstandard2.0`, `netstandard2.1`, `net472`, `net48`, and `net6.0` through `net10.0` | `pwsh -File tests/verify-package-compatibility.ps1` |
| AC-01 | Conditional package boundary | Each package contains nine exact runtime TFM directories and its analyzer closure; Pipeline exports `IsExternalInit` only for `netstandard2.0`, `netstandard2.1`, `net472`, and `net48`, exports both required-member attributes for those plus `net6.0`, exports none for `net7.0`–`net10.0`, and StateMachine exports none | Inspect `.nupkg` entries, resolved consumer assets, and every built runtime assembly's exported metadata during compatibility verification |
| AC-01 | Conditional regression | Existing generator/runtime tests and the trimmed .NET 10 consumer exercising both packages remain green | Run both Release test projects and publish/run the unified trimmed package consumer |

<!-- section: completion-criteria -->
## Completion

Complete when both locally packed packages expose the intended nine exact runtime assets, the entire supported consumer matrix compiles from those packages with both generators active, existing tests and two-package trimming proof pass, and durable compatibility documentation no longer claims runtime support is limited to .NET 10. Clean this temporary change record after reviewed implementation, commit, and explicit cleanup authorization.

## Candidate evidence

- Changed artifacts: both runtime project/package boundaries, Pipeline compiler support and generator calls, legacy compiler-feature support, the Pipeline analyzer asset list, the unified package consumer/verifier, dependency baselines, and the root compatibility statement.
- Primary and package-boundary proof: `tests/verify-package-compatibility.ps1` passed all nine exact consumer targets, analyzer closure checks, exported compiler-feature type checks, resolved-asset checks, and the trimmed two-package executable (`42:Completed`).
- Regression proof: Pipeline 173/173 and StateMachine 26/26 Release tests passed; the Release solution build passed with zero warnings and errors.
- Review correction: the stale Pipeline net10-only sentence found by independent review was replaced with the shared framework matrix; compatibility verification was rerun and both rebuilt packages were inspected to confirm the corrected README.
- Scope deviation, migration, or operational handoff: None. No persisted data or external system changed.
- Maintainability: no repository cognitive-complexity threshold exists; the new helpers use only guard or single preprocessor decisions and add no nested control flow.
