# Migrate Step objects to function declarations

Pipeline Steps no longer have runtime object identities. Upgrade the runtime and analyzer packages together, recompile consumers, and migrate declarations as follows.

## Leaf Steps

Replace a `ref struct` plus an execution interface with a static method marked `[Step]`. Constructor arguments become ordinary method parameters, injected dependencies receive `[FromServices]`, and the caller token remains the final unmarked `CancellationToken`.

```csharp
// Before
internal readonly ref partial struct Add(int left, int right) : IStep<int>
{
    public int Execute() => left + right;
}

// After
internal static class MathSteps
{
    [Step]
    internal static int Add(int left, int right, CancellationToken token) => left + right;
}
```

Use `Task` or `Task<TResult>` for asynchronous Steps. The former `IStep`, `IStep<TResult>`, `IAsyncStep`, and `IAsyncStep<TResult>` contracts are removed.

## Composite Steps and root Pipelines

Replace the Composite object and its primary-constructor inputs with a top-level static partial class containing `static void Configuration(StepGraph, ...)`. Parameters after `StepGraph` follow the same data, service, default-value, and cancellation rules as Leaf methods.

Add `[Pipeline]` only when callers need a root execution facade. The generated facade is nested in the containing class and defaults to `<method name>Pipeline`; `[Pipeline(Name = "Import")]` generates `ImportPipeline`.

```csharp
// Before
[CompositeStep]
public readonly ref partial struct Import(string path, int batchSize = 100)
{
    private void Configuration(StepGraph steps)
    {
        var rows = steps.Read(path);
        steps.Save(rows, batchSize);
    }
}

var oldPipeline = new Import.Pipeline(services);
var oldResults = oldPipeline.Execute(path: "input.csv");

// After
public static partial class Import
{
    [Pipeline]
    public static void Configuration(
        StepGraph steps,
        string path,
        int batchSize = 100,
        [FromServices] IStore store = null!,
        CancellationToken token = default)
    {
        var rows = steps.Read(path);
        steps.Save(rows, batchSize);
    }
}

var pipeline = new Import.ConfigurationPipeline(services);
var results = pipeline.Execute(path: "input.csv");
```

Pipeline constructors now receive only `IServiceProvider` when the selected root directly or transitively needs services. Business and defaulted inputs move to `Execute`/`ExecuteAsync`. A Step or Configuration without `[Pipeline]` remains composable through `StepGraph` but gets no root facade.

## Logging and display names

Remove `[StepLogger]`, generated Logger properties, and all `StepContext`/`StepExecutionInfo` usage. Request logging explicitly with an unkeyed `[FromServices] ILogger` parameter, or request `ILogger<T>` through DI.

`WithDisplayName` names only the current graph node. The generated category for non-generic `ILogger` contains the method identity and the complete slash-joined path, such as `MyApp.Steps.Save[Import/Validate/Save]`; display paths are not exposed to Step methods.

## Compatibility and recovery

This migration intentionally removes the object contracts, `[CompositeStep]`, `[StepLogger]`, `ICompositeStep<TResult>`, and `IAsyncCompositeStep<TResult>`. There is no compatibility adapter or reflection fallback. Public Composite composition across assemblies requires matching runtime/analyzer packages and consumer recompilation. To roll back, restore the previous matching package versions and the corresponding object declarations from source control.

When two referenced assemblies expose a Composite with the same namespace and type name, assign each project/package reference a distinct C# alias and call the generated assembly-qualified extension carrier. The generator emits matching `extern alias` qualifiers; without distinct aliases it reports the collision instead of guessing an assembly.
