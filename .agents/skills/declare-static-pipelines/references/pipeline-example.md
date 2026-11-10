# Complete typed pipeline example

```csharp
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.Orchestration.Pipeline;
using TedToolkit.Orchestration.Pipeline.Attributes;

var pipeline = new App.RunPipeline();
var results = pipeline.Execute(leftValue: 40);

Console.WriteLine(results.Sum.Add); // 42

public static partial class App
{
    [Pipeline]
    public static void Run(StepGraph pipeline, int leftValue)
    {
        var sum = pipeline.Calculate(leftValue);
    }
}

public static partial class Sum
{
    // A function whose first parameter is StepGraph is a Composite Step.
    // It remains reusable without a Pipeline attribute or generated facade class.
    public static void Calculate(StepGraph pipeline, int leftValue)
    {
        var left = pipeline.Value(leftValue);
        var right = pipeline.Value(2);
        var add = pipeline.Add(left, right);
    }
}

internal static class Steps
{
    [Step]
    internal static int Value(int value, CancellationToken token) => value;

    [Step]
    internal static int Add(int a, int b, CancellationToken token) => a + b;
}
```

The generated entry point and result shape follow the typed graph. An async child changes the root execution API to `ExecuteAsync`.
