# Provide verified package-specific onboarding

<!-- change-format: 3 -->
<!-- workflow-profile: standard -->
<!-- change-kind: maintenance -->
<!-- change-status: draft -->
<!-- delivery-shape: single -->

- Priority: P2
<!-- approval-source: none -->
<!-- candidate-binding: none -->

<!-- section: goal-rationale -->
## Goal and rationale

Give Pipeline and StateMachine NuGet consumers focused package documentation whose first-use examples are verified against the locally packed product. Both packages currently embed the same repository-wide README, so a package consumer must navigate unrelated material and example drift is not detected at the package boundary.

<!-- section: scope -->
## Scope and non-goals

- In scope: package-specific README content, installation and first-use paths, supported boundaries, links to deeper documentation, and compilation verification of representative examples.
- Non-goals: redesigning the repository README, changing public APIs, adding tutorials for every feature, publishing packages, or duplicating architecture records.
- Compatibility: package IDs, target frameworks, generated APIs, runtime behavior, analyzer behavior, and licensing remain unchanged.

<!-- section: structural-contract -->
## Structural outcome

<!-- structural-outcome: STR-01 -->
- STR-01: Each NuGet package embeds a focused README that identifies the package, installation boundary, smallest supported example, operational limits, and links to authoritative repository documentation.

<!-- structural-outcome: STR-02 -->
- STR-02: Representative first-use examples for both packages compile against the locally packed packages.

## Constraints and risks

- Keep one authoritative home for detailed semantics; package READMEs summarize and link instead of copying the full repository guide.
- Examples must consume packed artifacts rather than project references so analyzer/runtime packaging is exercised.
- Any example that requires a public API or behavior change is an escalation trigger.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

<!-- section: delivery-brief -->
## Delivery brief

- Outcome and target delivery area: package-owned onboarding content and bounded example verification against local packages.
- Other start conditions: public README and architecture records remain authoritative for detailed behavior.
- Likely touchpoints (non-binding): both runtime project files, package README files, root navigation, and package-consumer fixtures.
- Private implementation choices left open: README locations, fixture source layout, and how examples are shared with verification without making documentation generation complex.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: STR-01 purpose=structural shape=manual -->
<!-- primary-proof: STR-02 purpose=boundary shape=integration -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| STR-01 | Primary | Inspecting either package shows only relevant first-use guidance and working links to deeper documentation | Pack both products and inspect their embedded README metadata/content |
| STR-02 | Primary | Pipeline and StateMachine first-use consumers compile against the local packages without project references | Run bounded package-consumer compilation against Release packages |

<!-- section: completion-criteria -->
## Completion

Complete when both packed READMEs are focused, both representative consumers compile from local packages, repository navigation reaches the package guides, and candidate-bound review finds no duplicated authoritative semantics or shipped API change. Package guides remain durable; clean this temporary change record after merge and reference release.
