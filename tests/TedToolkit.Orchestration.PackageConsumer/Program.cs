using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TedToolkit.Orchestration.Pipeline;
using TedToolkit.Orchestration.Pipeline.Attributes;
using TedToolkit.Orchestration.StateMachine;

public static class Program
{
    public static async Task Main()
    {
        using var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton<IOffset, Offset>()
            .BuildServiceProvider();

        var result = new Root.ConfigurationPipeline(services).Execute(40);
        var machine = new OrderMachine();
        await machine.CompleteAsync();
        if (result.Inner.Adjusted != 42 || machine.State != OrderState.Completed)
            throw new InvalidOperationException("Package consumer produced an unexpected result.");

        Console.WriteLine($"{result.Inner.Adjusted}:{machine.State}");
    }
}

public interface IOffset
{
    int Value { get; }
}

public sealed class Offset : IOffset
{
    public int Value => 2;
}

internal static class PackageSteps
{
    [Step]
    internal static int AddOffset(
        int value,
        [FromServices] IOffset offset,
        [FromServices] ILogger logger,
        CancellationToken token)
    {
        logger.LogDebug("Executing package leaf");
        return value + offset.Value;
    }
}

public static partial class Inner
{
    [Pipeline]
    public static void Configuration(StepGraph steps, int value)
    {
        var adjusted = steps.AddOffset(value).WithDisplayName("Package leaf");
    }
}

public static partial class Root
{
    [Pipeline]
    public static void Configuration(StepGraph steps, int value)
    {
        var inner = steps.Inner(value);
    }
}

public enum OrderState
{
    Draft,
    Completed,
}

[StateMachine<OrderState>(OrderState.Draft)]
public sealed partial class OrderMachine
{
    [TransitionTo(OrderState.Completed, OrderState.Draft)]
    public partial ValueTask CompleteAsync();
}
