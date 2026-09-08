namespace TedToolkit.Orchestration.Pipeline;

/// <summary>Marks a generated synchronous Composite Step with a typed result.</summary>
/// <typeparam name="TResult">The generated Composite results type.</typeparam>
public interface ICompositeStep<TResult>;

/// <summary>Marks a generated asynchronous Composite Step with a typed result.</summary>
/// <typeparam name="TResult">The generated Composite results type.</typeparam>
public interface IAsyncCompositeStep<TResult>;
