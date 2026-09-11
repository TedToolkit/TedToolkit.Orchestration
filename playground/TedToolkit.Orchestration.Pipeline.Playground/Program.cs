using TedToolkit.Orchestration.Pipeline;
using TedToolkit.Orchestration.Pipeline.Attributes;
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.Orchestration.Pipeline.Playground;

Console.WriteLine("Pipeline playground.");
var services = new ServiceCollection();
services.AddScoped<IResultFormatter, ResultFormatter>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var executor = new DemoPipeline.RunPipeline(scope.ServiceProvider);
var first = await executor.ExecuteAsync(leftValue: 1, formatLabel: "sum");
Console.WriteLine($"{first.Calculation.Format} (value: {first.Calculation.Add})");
var second = await executor.ExecuteAsync(leftValue: 8, formatLabel: "again");
Console.WriteLine($"{second.Calculation.Format} (value: {second.Calculation.Add})");
await executor.ExecuteAsync(leftValue: 3, formatLabel: "ignored result");

/// <summary>Exposes the reusable Composite Step as a root Pipeline.</summary>
public static partial class DemoPipeline
{
    /// <summary>Declares the root graph and composes a reusable Composite Step.</summary>
    [Pipeline]
    public static void Run(StepGraph pipeline, int leftValue, string formatLabel)
    {
        var calculation = pipeline.Calculate(leftValue, formatLabel);
    }
}

/// <summary>A Composite Step: StepGraph identifies it; no Pipeline attribute is required.</summary>
public static partial class Calculation
{
    /// <summary>Declares the reusable child graph.</summary>
    public static void Calculate(StepGraph pipeline, int leftValue, string formatLabel)
    {
        var left = pipeline.DelayStep(leftValue).WithRetry(1).WithTimeout(2000);
        var right = pipeline.DelayStep(value: 2).WithRetry(1).WithTimeout(2000);
        var add = pipeline.AddStep(a: left, b: right);
        var format = pipeline.FormatStep(sum: add, label: formatLabel);
        pipeline.WriteStep(message: format);
    }
}




