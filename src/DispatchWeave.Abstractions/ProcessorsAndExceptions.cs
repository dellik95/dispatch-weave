namespace DispatchWeave;

/// <summary>
/// Defines the execution priority of a pipeline behavior. Lower values execute earlier (outermost).
/// </summary>
public interface IOrderedBehavior
{
    int Order { get; }
}

/// <summary>
/// Specifies the execution priority of a pipeline behavior. Lower values execute earlier (outermost).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class PipelineOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

/// <summary>
/// Executes custom logic before a request handler is invoked.
/// </summary>
public interface IRequestPreProcessor<in TRequest> where TRequest : notnull
{
    Task Process(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes custom logic after a request handler completes successfully.
/// </summary>
public interface IRequestPostProcessor<in TRequest, in TResponse> where TRequest : notnull
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken = default);
}

/// <summary>
/// Holds the handling state for a caught exception during request execution.
/// </summary>
public sealed class RequestExceptionHandlerState<TResponse>
{
    public bool Handled { get; private set; }
    public TResponse? Response { get; private set; }

    /// <summary>
    /// Marks the exception as handled and supplies the fallback response.
    /// </summary>
    public void SetHandled(TResponse response)
    {
        Handled = true;
        Response = response;
    }
}

/// <summary>
/// Handles an exception of type <typeparamref name="TException"/> thrown during processing of <typeparamref name="TRequest"/>,
/// optionally supplying a fallback <typeparamref name="TResponse"/> via <see cref="RequestExceptionHandlerState{TResponse}.SetHandled"/>.
/// </summary>
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TRequest : IRequest<TResponse>
    where TException : Exception
{
    Task Handle(
        TRequest request,
        TException exception,
        RequestExceptionHandlerState<TResponse> state,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes a side-effect action (e.g., auditing, alerting) when an exception of type <typeparamref name="TException"/>
/// is thrown while handling <typeparamref name="TRequest"/>, without swallowing the exception.
/// </summary>
public interface IRequestExceptionAction<in TRequest, in TException>
    where TRequest : notnull
    where TException : Exception
{
    Task Execute(TRequest request, TException exception, CancellationToken cancellationToken = default);
}
