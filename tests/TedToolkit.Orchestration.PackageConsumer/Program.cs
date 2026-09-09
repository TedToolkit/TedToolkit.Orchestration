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

        var result = new Root.Pipeline(services).Execute(40);
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

[StepLogger]
internal readonly ref partial struct AddOffset(
    int value,
    [FromServices] IOffset offset) : IStep<int>
{
    public int Execute(CancellationToken token)
    {
        Logger.LogDebug("Executing {DisplayName}", DisplayName);
        return value + offset.Value;
    }
}

[CompositeStep]
public readonly ref partial struct Inner(int value)
{
    private void Configuration(StepGraph steps)
    {
        var adjusted = steps.AddOffset(value).WithDisplayName("Package leaf");
    }
}

[CompositeStep]
public readonly ref partial struct Root(int value)
{
    private void Configuration(StepGraph steps)
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
