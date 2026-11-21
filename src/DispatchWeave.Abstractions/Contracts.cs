namespace DispatchWeave;

/// <summary>
/// Marker interface for a request that produces a response of type <typeparamref name="TResponse"/>.
/// </summary>
public interface IRequest<out TResponse>;

/// <summary>
/// Marker interface for a command/request that produces no return value (<see cref="Unit"/>).
/// </summary>
public interface IRequest : IRequest<Unit>;

/// <summary>
/// Marker interface for a streaming request that produces an asynchronous stream of <typeparamref name="TResponse"/> items.
/// </summary>
public interface IStreamRequest<out TResponse>;

/// <summary>
/// Marker interface for an in-process notification event.
/// </summary>
public interface INotification;

/// <summary>
/// Handles a request of type <typeparamref name="TRequest"/> and returns <typeparamref name="TResponse"/>.
/// </summary>
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles a void request of type <typeparamref name="TRequest"/> that returns <see cref="Unit"/>.
/// </summary>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit> where TRequest : IRequest<Unit>
{
    async Task<Unit> IRequestHandler<TRequest, Unit>.Handle(TRequest request, CancellationToken cancellationToken)
    {
        await Handle(request, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }

    new Task Handle(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles a streaming request of type <typeparamref name="TRequest"/> and yields <typeparamref name="TResponse"/> items.
/// </summary>
public interface IStreamRequestHandler<in TRequest, out TResponse> where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles an in-process notification of type <typeparamref name="TNotification"/>.
/// </summary>
public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the next continuation in a request pipeline.
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Represents the next continuation in a streaming request pipeline.
/// </summary>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>();

/// <summary>
/// Pipeline behavior wrapping the execution of a request handler.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline behavior wrapping the execution of a streaming request handler.
/// </summary>
public interface IStreamPipelineBehavior<in TRequest, TResponse> where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends single-response and streaming requests through the configured pipeline.
/// </summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes notifications to all subscribed handlers.
/// </summary>
public interface IPublisher
{
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    Task Publish(object notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Unified dispatcher combining <see cref="ISender"/> and <see cref="IPublisher"/>.
/// </summary>
public interface IDispatcher : ISender, IPublisher;

/// <summary>
/// Boundary interface to integrate a dependency injection container or an explicit handler registry.
/// </summary>
public interface IDispatchRoutes
{
    RequestHandlerDelegate<TResponse> Handler<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IRequest<TResponse>;

    IEnumerable<IPipelineBehavior<TRequest, TResponse>> Behaviors<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>;

    IEnumerable<INotificationHandler<TNotification>> Notifications<TNotification>()
        where TNotification : INotification;

    StreamHandlerDelegate<TResponse> StreamHandler<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IStreamRequest<TResponse> =>
        throw HandlerResolutionException.Missing(typeof(TRequest));

    IEnumerable<IStreamPipelineBehavior<TRequest, TResponse>> StreamBehaviors<TRequest, TResponse>()
        where TRequest : IStreamRequest<TResponse> => [];
}

/// <summary>
/// Strategy interface for dispatching a notification to a set of resolved handlers.
/// </summary>
public interface INotificationPublisher
{
    Task Publish<T>(IEnumerable<INotificationHandler<T>> handlers, T notification, CancellationToken token)
        where T : INotification;
}
