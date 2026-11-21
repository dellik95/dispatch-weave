namespace DispatchWeave.Registration;

public sealed class RegistryRoutes(HandlerRegistry handlers, PipelineRegistry behaviors, NotificationRegistry notifications,
    bool lastBehaviorOutermost = false) : IDispatchRoutes
{
    public RequestHandlerDelegate<TResponse> Handler<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IRequest<TResponse> => handlers.Resolve<TRequest, TResponse>(request, token);

    public IEnumerable<IPipelineBehavior<TRequest, TResponse>> Behaviors<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var pipeline = behaviors.GetBehaviors<TRequest, TResponse>();
        return lastBehaviorOutermost ? pipeline.Reverse() : pipeline;
    }

    public IEnumerable<INotificationHandler<T>> Notifications<T>() where T : INotification =>
        notifications.GetHandlers<T>();
}
