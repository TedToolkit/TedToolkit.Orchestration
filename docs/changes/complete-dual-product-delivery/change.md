# Complete dual-product delivery through TedToolkit

<!-- change-format: 3 -->
<!-- workflow-profile: controlled -->
<!-- change-kind: behavior-change -->
<!-- change-status: in-progress -->
<!-- delivery-shape: single -->

- Priority: P0
<!-- approval-source: user-approved-and-directed-immediate-completion-in-codex-task-2026-09-06 -->
<!-- candidate-binding: none -->

<!-- section: goal-rationale -->
## Goal and rationale

Make the current repository's Build project the single CI/CD authority for Pipeline and StateMachine while consuming the checked-in TedToolkit modules unchanged. GitHub Actions currently duplicates product commands, omits the StateMachine suite, and does not provide a dependable test/package barrier before TedToolkit's GitHub-side operations.

<!-- section: scope -->
## Scope and non-goals

- In scope: the repository Build-project configuration, one repository-owned delivery-gate adapter, GitHub Actions event/permission isolation, both test executables, both NuGet products, and package-content/consumer verification.
- Non-goals: editing any file under `externals/TedToolkit`, advancing its submodule pointer, reimplementing TedToolkit build/test/pack/push/release modules, changing product runtime behavior, publishing from local or validation-only runs, benchmark gates, or manual version changes.
- Compatibility: public APIs, analyzer diagnostics, target frameworks, package IDs, runtime/analyzer bundling, and existing test behavior remain unchanged.

<!-- section: behavior-contract -->
## Behavior contract

<!-- behavior-change: OB-01 -->
| ID | Observable boundary | Current | Expected | Preserved |
| --- | --- | --- | --- | --- |
| OB-01 | Repository CI/CD | YAML directly builds/tests/packs one product and the shared release graph lacks a repository test/package barrier | YAML selects a trust mode and invokes the repository Build project; unchanged TedToolkit modules perform CI/CD for both products behind a repository-owned gate | Product contracts and TedToolkit source/submodule revision remain unchanged |

<!-- acceptance-case: AC-01 -->
### AC-01 — One authoritative C# pipeline

```gherkin
Scenario: Workflow delegates product delivery
  Given any supported local or GitHub invocation
  When repository CI/CD runs
  Then build, test, package, push, pull-request, and release work is performed by the Build project through unchanged TedToolkit modules, with no duplicate product command sequence in YAML or scripts
```

<!-- acceptance-case: AC-02 -->
### AC-02 — Both products are gated

```gherkin
Scenario: Verify Pipeline and StateMachine delivery artifacts
  Given a Release pipeline execution
  When the repository delivery gate completes
  Then both configured test suites have complete passing reports and both NuGet products contain their matching runtime and analyzer and support the bounded consumer check
```

<!-- acceptance-case: AC-03 -->
### AC-03 — Failed or incomplete verification blocks external effects

```gherkin
Scenario: Stop delivery before an external operation
  Given a build failure, failed or missing configured test report, or invalid package product
  When the TedToolkit module graph reaches the repository delivery gate
  Then no pull-request mutation, NuGet push, or GitHub Release operation can start
```

<!-- acceptance-case: AC-04 -->
### AC-04 — Event, branch, permission, and effect matrix

| Event and ref | Mode | Credentials | Allowed external effect |
| --- | --- | --- | --- |
| Pull request, feature-branch push, or manual dispatch | Validation only | `contents: read`; no other write permission, AI credential, or NuGet publishing credential | None |
| Push to `development` | Delivery after the same execution's gate | `contents: read` and `pull-requests: write`; no other write permission, AI credential, or NuGet publishing credential | Create or retain the draft release pull request to `main`; no other pull-request mutation, package push, or GitHub Release |
| Push to `main` | Delivery after the same execution's gate | `contents: write` plus NuGet publishing configuration; no other write permission or AI credential | NuGet push followed by GitHub Release; no pull-request mutation |

Any event/ref combination not listed for delivery is validation-only. Unlisted GitHub write permissions are absent. AI credentials are absent in every mode so AI-dependent modules cannot broaden the matrix. Concurrent delivery for the same ref is serialized so version and release decisions cannot race.

## Constraints and risks

- `externals/TedToolkit/**` and the recorded TedToolkit submodule commit are immutable for this change.
- Reuse `TedPipeline`, `PipelineFiles`, the existing TedToolkit modules, shared NuGet properties, output layout, GitHub conditions, push implementation, and release implementation.
- The repository-owned gate may depend on TedToolkit's test module, parse its produced reports through the existing parser, and verify repository-specific package contents/consumer compatibility. It must be a dependency of every external-effect module; it must not reproduce the underlying build, test, pack, push, pull-request, or release actions.
- GitHub Actions owns only event selection, least-privilege credentials, delivery concurrency, checkout/runtime setup, Build-project invocation, and output upload.
- Validation-only execution must make TedToolkit GitHub-only modules ineligible and must receive no publishing or AI credentials. Credentialed delivery receives only the exact matrix permissions and secrets; no mode receives AI credentials. Local proof must never contact NuGet publishing or mutate GitHub.
- The accepted delivery boundary is recorded in `docs/architecture/delivery-system.md`; divergence requires architecture and change review.
- Changes to TedToolkit source/revision, product contracts, package identity, branch names, credential scope, external effects, or the release/version algorithm require renewed approval.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

None. The repository owns the adapter and workflow boundary; no upstream TedToolkit change is required.

<!-- section: delivery-brief -->
## Delivery brief

- Outcome and target delivery area: configure both products in the existing Build project, add the smallest repository-specific gate needed by the unchanged TedToolkit graph, and replace handwritten YAML product commands with trust-mode invocations of that Build project.
- Baseline evidence: Release build and both suites passed locally (Pipeline 133/133; StateMachine 26/26 on 2026-09-05); the TedToolkit submodule is clean at `b9152e16ef9e227a2540b86b90af5391374cfd90`.
- Likely touchpoints (non-binding): `.github/workflows/build.yml`, `build/TedToolkit.Orchestration.Build/`, solution/test infrastructure, and package-verification support outside `externals/TedToolkit`.
- Private implementation choices left open: gate/helper/test layout and the bounded temporary consumer shape, provided the dependency barrier and no-duplication constraints remain observable.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: AC-01 purpose=structural shape=integration -->
<!-- primary-proof: AC-02 purpose=boundary shape=integration -->
<!-- primary-proof: AC-03 purpose=regression shape=integration -->
<!-- primary-proof: AC-04 purpose=boundary shape=manual -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| AC-01 | Primary | Workflow and launchers contain no product build/test/pack/release command sequence and invoke only the repository Build project | Run the local launcher and inspect workflow command boundaries against the Build project |
| AC-02 | Primary | The gate observes both passing suites and verifies both runtime/analyzer package products plus the bounded consumer check | Run the repository Build project locally in Release mode and inspect `output/` and test reports |
| AC-03 | Primary | Synthetic failed/missing test-report and invalid-package cases fail the gate; graph validation shows every external-effect module depends on it | Run the Build-project gate tests and the pipeline graph validation test |
| AC-04 | Primary | Workflow job conditions, `CI` mode, permissions, secrets, and concurrency match the approved matrix; an authorized remote run confirms the applicable effect | Review the workflow statically, then inspect one validation-only run and one intended branch delivery run |

## Operational handoff

- Owner: repository maintainer.
- Configure NuGet secrets/variables only for the `main` job, use only the exact matrix permissions, protect `development` and `main`, and provide no AI credentials. Keep validation-only jobs read-only and publishing-secret-free.
- Closure evidence: one validation-only GitHub run shows no external-effect module eligible; one authorized `development` or `main` run shows the expected gate-before-effect order. A real NuGet push or GitHub Release is required only from the intended `main` delivery, never from local proof.

<!-- section: completion-criteria -->
## Completion

Complete when the local candidate passes both suites and package/consumer verification through the Build project; candidate-bound review proves the external-effect dependency barrier, immutable TedToolkit submodule, and absence of duplicate CI/CD actions; workflow policy matches the approved matrix; and the repository maintainer closes the remote configuration/run handoff. The durable delivery boundary remains in `docs/architecture/delivery-system.md`; clean this temporary change record after merge and release reference.
