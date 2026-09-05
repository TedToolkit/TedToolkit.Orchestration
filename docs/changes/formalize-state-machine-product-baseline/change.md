# Formalize the StateMachine product baseline

<!-- change-format: 3 -->
<!-- workflow-profile: standard -->
<!-- change-kind: maintenance -->
<!-- change-status: draft -->
<!-- delivery-shape: single -->

- Priority: P1
<!-- approval-source: none -->
<!-- candidate-binding: none -->

<!-- section: goal-rationale -->
## Goal and rationale

Turn the already approved StateMachine direction into a durable product baseline that future changes can consume without relying on an originating conversation. The StateMachine architecture record currently names no applicable product intent, and the existing design-principles record is formally scoped only to Pipeline.

<!-- section: scope -->
## Scope and non-goals

- In scope: StateMachine consumers, problem/value, boundaries, success signals, review triggers, and explicit principle applicability.
- Non-goals: changing StateMachine behavior, reopening already approved architecture, introducing new features, or generalizing Pipeline product intent.
- Compatibility: all documented StateMachine APIs, generated behavior, diagnostics, lifecycle ordering, event timing, and caller-owned concurrency remain unchanged.

<!-- section: structural-contract -->
## Structural outcome

<!-- structural-outcome: STR-01 -->
- STR-01: A durable approved StateMachine product-intent record identifies its target consumers, value, supported boundary, explicit non-goals, success evidence, and review triggers.

<!-- structural-outcome: STR-02 -->
- STR-02: The governing principles explicitly state which requirements apply to StateMachine, without implying that Pipeline-only execution policies govern it.

## Constraints and risks

- Extract only direction already evidenced by the README and accepted StateMachine architecture record; any new product capability or changed boundary is an escalation trigger.
- Keep product intent distinct from implementation architecture and avoid copying detailed trigger, guard, lifecycle, and emitter mechanics into the product record.
- A conflict between current public documentation and the accepted architecture is an escalation trigger rather than an invitation to choose one silently.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

None. Ready from the approved baseline.

<!-- section: delivery-brief -->
## Delivery brief

- Outcome and target delivery area: durable product and principle records for StateMachine.
- Other start conditions: existing approved StateMachine architecture and public README are the evidence baseline.
- Likely touchpoints (non-binding): `docs/product/`, `docs/principles/`, `docs/architecture/state-machine-system.md`, and navigation links.
- Private implementation choices left open: whether shared principles are generalized or StateMachine-specific principles are recorded separately.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: STR-01 purpose=structural shape=manual -->
<!-- primary-proof: STR-02 purpose=structural shape=manual -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| STR-01 | Primary | A five-minute reader can identify StateMachine purpose, boundaries, success evidence, and review triggers without the originating task | Compare the product record against the public README and accepted architecture record |
| STR-02 | Primary | Each applicable principle has explicit StateMachine scope and no Pipeline-only rule is accidentally inherited | Review the principles and architecture links, then run the repository documentation/build gate |

<!-- section: completion-criteria -->
## Completion

Complete when the durable product/principle records contain the current approved truth, architecture links resolve to them, and review finds no new behavior decision hidden in the documentation. The durable records remain; clean this temporary change record after merge and reference release.
