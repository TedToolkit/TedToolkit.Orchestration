---
name: declare-static-pipelines
description: >-
  Use TedToolkit.Orchestration.Pipeline to declare compile-time in-process orchestration with typed inputs, dependencies, results, and generated execution.
---

# Declare Static Pipelines

Use a static pipeline when the graph topology is known at compile time and typed outputs must connect steps. Identify the inputs, dependencies, and observable execution order before changing the graph.

## Workflow

- Declare the entry point with `[Pipeline]` on a static partial type and describe composition through `StepGraph`. Use typed graph outputs as the inputs to dependent steps.
- Keep the graph topology visible to the compiler. Put dynamic business decisions inside steps and use the package’s declared dependency and execution-policy mechanisms for the graph.
- Build the consumer and use the generated `Execute` or `ExecuteAsync` entry point that matches the graph. Inspect named results and verify dependency ordering, failures, and cancellation that matter to the caller.

## Example

```csharp
var prepare = pipeline.Prepare();
var work = pipeline.Work()
    .DependsOn(prepare)
    .WithRetry(2)
    .WithTimeout(5_000)
    .WithDisplayName("Main work");
```

## Check the result

Build and inspect the generated result and execution API. Verify async behavior, dependency ordering, and which branches can run concurrently. See [the complete typed pipeline example](references/pipeline-example.md) when adding a new graph.
