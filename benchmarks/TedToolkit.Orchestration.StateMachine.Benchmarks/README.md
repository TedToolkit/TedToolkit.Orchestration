# StateMachine benchmarks

Compare the generated StateMachine hot path with handwritten code and two general-purpose NuGet libraries:

- TedToolkit.Orchestration.StateMachine from this workspace
- Stateless 5.20.1
- Appccelerate.StateMachine 6.0.0
- a handwritten enum switch baseline

The maintained workloads cover one observable toggle transition, a guarded toggle with entry and exit actions, capability queries where the compared library exposes one, and native construction/setup. Setup is kept outside execution measurements. Construction results are reported separately because the libraries expose different configuration and definition-reuse models.

All adapters must pass `--verify` before measurement. Results apply only to the recorded .NET runtime and host; a microbenchmark does not rank hierarchy, persistence, graph export, thread safety, or other unmatched features.

See the [latest measured comparison](library-comparison.md), including raw reports and interpretation limits.

## Reproduce

```shell
dotnet run --project benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks --configuration Release -- --verify
dotnet run --project benchmarks/TedToolkit.Orchestration.StateMachine.Benchmarks --configuration Release --no-build -- --filter '*' --launchCount 2 --warmupCount 5 --iterationCount 15 --iterationTime 250
```

Package sources: [Stateless](https://www.nuget.org/packages/Stateless/5.20.1) and [Appccelerate.StateMachine](https://www.nuget.org/packages/Appccelerate.StateMachine/6.0.0).
