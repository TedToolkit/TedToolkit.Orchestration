namespace TedToolkit.Orchestration.Pipeline;

/// <summary>Executes one synchronous attempt without a result.</summary>
public interface IStep
{
    /// <summary>Executes the operation on the calling thread.</summary>
    /// <param name="cancellationToken">Cooperative cancellation for this attempt.</param>
    void Execute(CancellationToken cancellationToken = default);
}

/// <summary>Executes one synchronous attempt producing a result.</summary>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IStep<TResult>
{
    /// <summary>Executes the operation directly, without a task wrapper.</summary>
    /// <param name="cancellationToken">Cooperative cancellation for this attempt.</param>
    /// <returns>The operation's result.</returns>
    TResult Execute(CancellationToken cancellationToken = default);
}
