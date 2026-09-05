namespace TedToolkit.Orchestration.Pipeline;

/// <summary>Starts one asynchronous attempt without retaining the step across suspension.</summary>
public interface IAsyncStep
{
    /// <summary>Starts the operation. The returned task owns all state needed until completion.</summary>
    /// <param name="cancellationToken">Cooperative cancellation for this attempt.</param>
    /// <returns>The operation's completion task.</returns>
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Starts one asynchronous attempt producing a result.</summary>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IAsyncStep<TResult>
{
    /// <summary>Starts the operation without retaining the ref struct instance.</summary>
    /// <param name="cancellationToken">Cooperative cancellation for this attempt.</param>
    /// <returns>The operation's result task.</returns>
    Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
