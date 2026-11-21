namespace DispatchWeave.Registration;

public class RegistryRoutes : IDispatchRoutes
{
    private readonly HandlerRegistry handlers;
    private readonly PipelineRegistry behaviors;
    private readonly NotificationRegistry notifications;

    internal bool LastBehaviorOutermost { get; }
    public DispatchWeaveOptions Options { get; }

    public RegistryRoutes(
        HandlerRegistry handlers,
        PipelineRegistry behaviors,
        NotificationRegistry notifications,
        bool lastBehaviorOutermost = false)
        : this(handlers, behaviors, notifications, new DispatchWeaveOptions { LastBehaviorOutermost = lastBehaviorOutermost })
    {
        LastBehaviorOutermost = lastBehaviorOutermost;
    }

    public RegistryRoutes(
        HandlerRegistry handlers,
        PipelineRegistry behaviors,
        NotificationRegistry notifications,
        DispatchWeaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(behaviors);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(options);

        this.handlers = handlers;
        this.behaviors = behaviors;
        this.notifications = notifications;
        Options = options;
        LastBehaviorOutermost = options.LastBehaviorOutermost;
    }

    public RequestHandlerDelegate<TResponse> Handler<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IRequest<TResponse> =>
        handlers.Resolve<TRequest, TResponse>(request, token);

    public virtual IEnumerable<IPipelineBehavior<TRequest, TResponse>> Behaviors<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var pipeline = behaviors.GetBehaviors<TRequest, TResponse>(
            includeOpen: true,
            includeInherited: Options.IncludeInheritedBehaviors);
        return LastBehaviorOutermost ? pipeline.Reverse() : pipeline;
    }

    public IEnumerable<INotificationHandler<T>> Notifications<T>() where T : INotification =>
        notifications.GetHandlers<T>();

    public StreamHandlerDelegate<TResponse> StreamHandler<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IStreamRequest<TResponse> =>
        handlers.ResolveStream<TRequest, TResponse>(request, token);

    public virtual IEnumerable<IStreamPipelineBehavior<TRequest, TResponse>> StreamBehaviors<TRequest, TResponse>()
        where TRequest : IStreamRequest<TResponse>
    {
        var pipeline = behaviors.GetStreamBehaviors<TRequest, TResponse>(
            includeOpen: true,
            includeInherited: Options.IncludeInheritedBehaviors);
        return LastBehaviorOutermost ? pipeline.Reverse() : pipeline;
    }
}
