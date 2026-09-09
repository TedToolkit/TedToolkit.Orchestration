using TedToolkit.Orchestration.Pipeline;
using TedToolkit.Orchestration.Pipeline.Attributes;
using Microsoft.Extensions.DependencyInjection;
using TedToolkit.Orchestration.Pipeline.Playground;

Console.WriteLine("Pipeline playground.");
var services = new ServiceCollection();
services.AddScoped<IResultFormatter, ResultFormatter>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var executor = new DemoPipeline.Pipeline(scope.ServiceProvider);
var first = await executor.ExecuteAsync(leftValue: 1, formatLabel: "sum");
Console.WriteLine($"{first.Format} (value: {first.Add})");
var second = await executor.ExecuteAsync(leftValue: 8, formatLabel: "again");
Console.WriteLine($"{second.Format} (value: {second.Add})");
await executor.ExecuteWithoutResultsAsync(leftValue: 3, formatLabel: "no snapshot");

/// <summary>Its execution parameters and typed results are generated from this configuration.</summary>
[CompositeStep]
public readonly ref partial struct DemoPipeline(int leftValue, string formatLabel)
{
    private void Configuration(StepGraph pipeline)
    {
        var left = pipeline.DelayStep(leftValue).WithRetry(1).WithTimeout(2000);
        var right = pipeline.DelayStep(value: 2).WithRetry(1).WithTimeout(2000);
        var add = pipeline.AddStep(a: left, b: right);
        var format = pipeline.FormatStep(sum: add, label: formatLabel);
        pipeline.WriteStep(message: format);
    }
}




