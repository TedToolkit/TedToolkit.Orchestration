# Modularize the StateMachine generator without behavior change

<!-- change-format: 3 -->
<!-- workflow-profile: standard -->
<!-- change-kind: behavior-preserving-refactor -->
<!-- change-status: completed -->
<!-- delivery-shape: single -->

- Priority: P1
<!-- approval-source: user-confirmed:2026-09-09 -->
<!-- candidate-binding: workspace:56343aac9dbc9937dec960e250af23a27c0c4874:sha256:62d3234bc1eb34a94fd8c8a5ad958012c32e2ff6c7a9bcd35a4a605c2cfa3186 -->

<!-- section: goal-rationale -->
## Goal and rationale

Separate StateMachine declaration discovery, semantic validation/model construction, and source emission so maintainers can change one concern without navigating a single 761-line generator that currently owns all three. The result should reduce change collision and review cost while preserving every consumer-visible contract.

<!-- section: scope -->
## Scope and non-goals

- In scope: internal StateMachine analyzer/generator responsibility boundaries and proportionate test organization needed to preserve them.
- Non-goals: changing supported declaration shapes, diagnostics, generated public API, generated execution semantics, runtime behavior, or optimizing benchmark results.
- Compatibility: consumer source compatibility, package composition, diagnostic IDs/severity/messages, generated names/signatures, guard/lifecycle binding, transition order, event timing, reentry behavior, and caller-owned concurrency remain unchanged.

<!-- section: invariants -->
## Preserved invariants

<!-- preserved-invariant: INV-01 -->
- INV-01: Every currently valid or invalid StateMachine declaration produces the same observable generated API, diagnostics, and transition behavior after the refactor.

<!-- preserved-invariant: INV-02 -->
- INV-02: The StateMachine NuGet package retains the same runtime/analyzer boundary and remains consumable from the locally packed artifacts.

## Constraints and risks

- Preserve symbol-based binding and the compile-time rejection model from `docs/architecture/state-machine-system.md`.
- Do not introduce runtime graph interpretation, reflection, shared mutable generator state, or a new public dependency.
- Any required diagnostic, generated signature, lifecycle order, package asset, or behavior change is an escalation trigger.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

<!-- section: delivery-brief -->
## Delivery brief

- Outcome and target delivery area: cohesive internal phases for StateMachine generator discovery/modeling/emission.
- Other start conditions: the accepted architecture record remains authoritative.
- Likely touchpoints (non-binding): `src/TedToolkit.Orchestration.StateMachine.Analyzer/StateMachineGenerator.cs`, internal model/emitter files, and StateMachine generator tests.
- Private implementation choices left open: exact internal types, file split, helper naming, test file split, and edit order.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: INV-01 purpose=regression shape=component -->
<!-- primary-proof: INV-02 purpose=boundary shape=integration -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| INV-01 | Primary | All StateMachine generation, diagnostic, lifecycle, event, guard, rejection, and reentry cases pass unchanged | `dotnet run --project tests/TedToolkit.Orchestration.StateMachine.Tests -c Release` |
| INV-02 | Primary | The packed StateMachine runtime/analyzer pair retains the expected package boundary | Build Release packages and inspect the runtime/analyzer entries |

<!-- section: completion-criteria -->
## Completion

Complete when internal responsibilities are separated, both invariant proofs pass on the exact candidate, and review confirms that no public or generated contract changed. No durable documentation update is required unless the implementation reveals a real architecture change, which would require renewed approval.

## Candidate result

- Baseline: 56343aac9dbc9937dec960e250af23a27c0c4874.
- Changed artifacts: the generator entry, semantic model, and source emission partial files under src/TedToolkit.Orchestration.StateMachine.Analyzer.
- INV-01: the StateMachine test project discovered and passed 26 of 26 tests with no failures or skips.
- INV-02: the Release package contains the net10.0 runtime and analyzers/dotnet/cs analyzer entries with no RoslynHelper payload.
- Conditional verification: whitespace validation and the Release solution build passed with zero build warnings and errors.
- Scope deviations, migration work, durable documentation changes, temporary artifacts, and known remaining implementation risks: none.

## Review result

- Compact delivery-candidate review concluded Ready to merge against the recorded workspace binding.
- Code correctness, test adequacy, and candidate-bound verification passed with no blocking or important findings and no design deviations.
- Existing architecture documentation remains current; no durable extraction or operational handoff is required.

## Completion result

- All completion criteria are satisfied on the recorded candidate; durable extraction and operational handoffs are not needed.
