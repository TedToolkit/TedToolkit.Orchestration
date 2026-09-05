# Complete dual-product delivery through TedToolkit

<!-- change-format: 3 -->
<!-- workflow-profile: standard -->
<!-- change-kind: maintenance -->
<!-- change-status: approved -->
<!-- delivery-shape: single -->

- Priority: P0
<!-- approval-source: user-approved-current-contract-in-codex-task-2026-09-05 -->
<!-- candidate-binding: none -->

<!-- section: goal-rationale -->
## Goal and rationale

Make the existing TedToolkit build pipeline execute and deliver both Pipeline and StateMachine products from one authoritative CI/CD path. The repository currently has a TedToolkit `TedPipeline` build project, but GitHub Actions duplicates build, test, and pack commands; the build project's `PipelineFiles` also lists only Pipeline tests. This leaves 26 passing StateMachine tests outside the authoritative test gate and lets YAML delivery behavior drift from TedToolkit.

<!-- section: scope -->
## Scope and non-goals

- In scope: the repository's TedToolkit build-project configuration, thin local/CI launchers, both test executables, both NuGet packages, and repository-specific package-content/consumer verification executed through TedToolkit.
- Non-goals: reimplementing TedToolkit modules in YAML or scripts, replacing or locally forking TedToolkit CI/CD behavior, changing Pipeline or StateMachine runtime behavior, publishing during local proof, adding benchmark timing gates, or changing package versions.
- Compatibility: public APIs, analyzer diagnostics, target frameworks, package IDs, runtime/analyzer bundling, and existing Pipeline test behavior remain unchanged.

<!-- section: structural-contract -->
## Structural outcome

<!-- structural-outcome: STR-01 -->
- STR-01: Local launchers and GitHub Actions delegate build, test, pack, and release decisions to the repository's single TedToolkit build project; workflow YAML contains no duplicate product build/test/pack/release sequence.

<!-- structural-outcome: STR-02 -->
- STR-02: The TedToolkit pipeline executes both repository test suites, creates both NuGet products through TedToolkit's existing package-on-build/output conventions, and runs repository-specific verification that each contains its matching runtime/analyzer and supports a minimal .NET 10 consumer compilation.

## Constraints and risks

- Preserve the architecture contract that each runtime package ships its matching analyzer; Pipeline analyzer dependencies remain private build assets.
- Use the existing `TedPipeline`, `PipelineFiles`, module dependency graph, shared NuGet MSBuild properties, output layout, GitHub-only release guards, push module, and release module as the authoritative CI/CD implementation.
- Package verification must be repository-specific work invoked by TedToolkit, use locally produced artifacts, and must not publish or contact an external package feed beyond ordinary restore dependencies.
- If TedToolkit lacks a required reusable capability, stop and propose a separate upstream TedToolkit change instead of implementing a parallel local pipeline.
- A discovery that package consumption requires a public API, target-framework, package-ID, analyzer dependency, or TedToolkit module behavior change is an escalation trigger requiring renewed approval.

<!-- section: start-conditions -->
## Start conditions

<!-- change-prerequisite: none -->

None. Ready from the approved baseline.

<!-- section: delivery-brief -->
## Delivery brief

- Outcome and target delivery area: configure the existing TedToolkit build entry point as the sole CI/CD path and add only the smallest repository-specific verification boundary it invokes.
- Other start conditions: Release build and both current suites pass locally (Pipeline 133/133; StateMachine 26/26 on 2026-09-05).
- Likely touchpoints (non-binding): `.github/workflows/build.yml`, `build/TedToolkit.Orchestration.Build/Program.cs`, TedToolkit shared NuGet property imports, solution/test infrastructure, and package verification support.
- Private implementation choices left open: repository-specific verification project/module shape and temporary consumer layout; no private choice may duplicate TedToolkit orchestration.

<!-- section: proof-plan -->
## Proof

<!-- primary-proof: STR-01 purpose=structural shape=integration -->
<!-- primary-proof: STR-02 purpose=boundary shape=integration -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| STR-01 | Primary | GitHub Actions and local launchers invoke the TedToolkit build project once, with no parallel handwritten product pipeline | Run `./build.ps1` on Windows or `./build.sh` on Unix and inspect the workflow delegation boundary |
| STR-02 | Primary | TedToolkit reports both suites successful and produces two verified package products; each matching runtime/analyzer supports a minimal consumer compilation | Run the repository TedToolkit build entry point with Release configuration and inspect its test/package verification results under `output/` |

<!-- section: completion-criteria -->
## Completion

Complete when local proof passes through TedToolkit, GitHub Actions is a thin invocation of that same build project, TedToolkit owns both suites and both package products through its existing modules, and candidate-bound review confirms no duplicated CI/CD implementation or shipped contract change. No durable documentation extraction is required; clean this temporary change record after merge and reference release under the repository workflow.
