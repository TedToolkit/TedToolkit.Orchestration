# Align delivery with TedToolkit

<!-- change-format: 3 -->
<!-- workflow-profile: controlled -->
<!-- change-kind: behavior-change -->
<!-- change-status: completed -->
<!-- delivery-shape: single -->
<!-- approval-source: user-directed-complete-removal-of-repository-delivery-system-in-codex-task-2026-09-06 -->
<!-- candidate-binding: commit:7e0465ae01ad073dbb4f31004a6a13655311f237 -->

<!-- section: goal-rationale -->
## Goal and rationale

Delete the repository-owned delivery system and use the same TedToolkit integration as sibling repositories. GitHub Actions delegates to TedToolkit's reusable workflow; this repository supplies only the conventional Build project and product package configuration.

<!-- section: scope -->
## Scope and non-goals

- In scope: the reusable-workflow call, `Build/Build.csproj`, `TedPipeline` solution/test configuration, product package imports and contents, matching solution/script paths, and removal of the repository-owned delivery gate, verifier, tests, and architecture record.
- Non-goals: changing `externals/TedToolkit`, adding another release module or verification layer, changing product APIs, or pushing/merging `main` in this delivery.
- Preserve both Pipeline and StateMachine tests and their runtime/analyzer package composition.

<!-- section: behavior-contract -->
<!-- behavior-change: OB-01 -->

<!-- acceptance-case: AC-01 -->
### AC-01 — Delegate GitHub delivery

```gherkin
Scenario: Repository workflow delegates to TedToolkit
  Given a push or manual workflow dispatch
  When GitHub Actions starts repository delivery
  Then the sole repository job calls TedToolkit/TedToolkit/.github/workflows/build.yml@development with the permissions and inherited secrets required by that reusable workflow and contains no repository-authored delivery steps
```

<!-- acceptance-case: AC-02 -->
### AC-02 — Configure rather than extend TedPipeline

```gherkin
Scenario: Build runs the existing TedToolkit pipeline
  Given Build/Build.csproj is invoked
  When the Build entry point executes
  Then it supplies the solution and both test projects to TedPipeline and calls ExecuteAsync without adding a repository module, gate, consumer verifier, or delivery implementation
```

The execution order, eligibility rules, credentials, pull-request behavior, package push, and GitHub Release effects are owned by the referenced TedToolkit workflow and modules. This repository deliberately adds no stronger ordering or credential boundary.

<!-- acceptance-case: AC-03 -->
### AC-03 — Package both products through shared props

```gherkin
Scenario: Release build produces both package layouts
  Given both product projects import the pinned NugetPackage.props
  When TedToolkit builds the solution in Release
  Then Pipeline contains its runtime, analyzer, RoslynHelper, ZString, and System.Memory assemblies and StateMachine contains its runtime and analyzer assemblies, with the shared icon and configured README
```

## Constraints and risks

- Keep the TedToolkit gitlink and every file below `externals/TedToolkit` unchanged.
- The reusable workflow intentionally floats on `@development`, matching sibling repositories; revalidate its resolved revision and contract on every delivery change.
- `secrets: inherit` and repository write permissions expose the credentials configured for the caller to TedToolkit. TedToolkit remains solely responsible for event/ref eligibility and effect ordering.
- A `development` push may create or retain the draft release pull request. This delivery must not push or merge `main`, publish NuGet packages, or create a GitHub Release.
- Any repository-owned delivery gate or change to package/public behavior requires renewed approval.

<!-- section: start-conditions -->
<!-- change-prerequisite: none -->

<!-- section: delivery-brief -->
## Delivery brief

Use the sibling reusable-workflow shape, move the existing Build project to the conventional path, reduce its entry point to configuration, import `NugetPackage.props` in both product projects, remove the superseded custom delivery implementation/tests/document, and update solution and launchers.

<!-- section: proof-plan -->
<!-- primary-proof: AC-01 purpose=boundary shape=manual -->
<!-- primary-proof: AC-02 purpose=acceptance shape=integration -->
<!-- primary-proof: AC-03 purpose=boundary shape=integration -->
| Contract | Role | Observable assertion | Command or bounded procedure |
| --- | --- | --- | --- |
| AC-01 | Primary | Workflow has one reusable-workflow job, no local steps, and a development push completes without a main release | Compare YAML with the resolved remote TedToolkit workflow, then inspect the authorized development run |
| AC-02 | Primary | Conventional Build entry runs both configured test projects through unchanged TedPipeline | Run `dotnet run --project Build/Build.csproj --configuration Release` with `CI=false` and all publishing credentials absent |
| AC-03 | Primary | Extracted packages contain exactly the required product/analyzer assets | Inspect `output/nuget/` after the local Build run |

## Operational handoff

- Owner: repository maintainer.
- The maintainer accepts the reusable workflow's `contents: write`, `pull-requests: write`, inherited-secrets boundary, floating `@development` reference, and TedToolkit-owned module ordering.
- Remote verification is limited to pushing the reviewed candidate to `development`, checking the resolved reusable-workflow run, and confirming no `main` publication or release occurred.

<!-- section: completion-criteria -->
## Completion

Complete when the minimal configuration is committed on `development`, the local Build passes both suites and package inspection, the TedToolkit submodule remains unchanged, and the pushed reusable-workflow run succeeds without a `main` release.
