---
name: declare-static-pipelines
description: >-
  Use TedToolkit.Orchestration.Pipeline to declare compile-time in-process orchestration with typed inputs, dependencies, results, and generated execution.
---

# Declare Static Pipelines

- Declare the entry point with `[Pipeline]` on a static partial type and describe composition through `StepGraph`. Use typed graph outputs as the inputs to dependent steps.
- Keep the graph topology visible to the compiler. Put dynamic business decisions inside steps and use the package’s declared dependency and execution-policy mechanisms for the graph.
- Build the consumer and use the generated `Execute` or `ExecuteAsync` entry point that matches the graph. Inspect named results and verify dependency ordering, failures, and cancellation that matter to the caller.
