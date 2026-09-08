using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TedToolkit.Orchestration.Pipeline;
using TedToolkit.Orchestration.Pipeline.Attributes;

var services = new ServiceCollection()
    .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
    .AddSingleton<IOffset, Offset>()
    .BuildServiceProvider();

var result = new Root.Pipeline(services).Execute(40);
if (result.Inner.Adjusted != 42)
    throw new InvalidOperationException("Package consumer produced an unexpected result.");

Console.WriteLine(result.Inner.Adjusted);

/// <summary>Supplies an offset to the package-consumer Step.</summary>
public interface IOffset
{
    /// <summary>Gets the offset.</summary>
    int Value { get; }
}

/// <summary>Default offset service.</summary>
public sealed class Offset : IOffset
{
    /// <inheritdoc />
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

/// <summary>Adds the injected offset.</summary>
/// <param name="value">The input value.</param>
[CompositeStep]
public readonly ref partial struct Inner(int value)
{
    private void Configuration(StepGraph steps)
    {
        var adjusted = steps.AddOffset(value).WithDisplayName("Package leaf");
    }
}

/// <summary>Exercises a public nested Composite from a package consumer.</summary>
/// <param name="value">The input value.</param>
[CompositeStep]
public readonly ref partial struct Root(int value)
{
    private void Configuration(StepGraph steps)
    {
        var inner = steps.Inner(value);
    }
}
