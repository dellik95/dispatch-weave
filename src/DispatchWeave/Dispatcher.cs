using System.Collections.Concurrent;

namespace DispatchWeave;

public sealed class Dispatcher(IDispatchRoutes routes, INotificationPublisher? publisher = null) : IDispatcher
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoke = RequestCache<TResponse>.Routes.GetOrAdd(request.GetType(), static type =>
            (RequestRoute<TResponse>)Activator.CreateInstance(typeof(RequestRoute<,>).MakeGenericType(type, typeof(TResponse)))!);
        return invoke.Send(routes, request, cancellationToken);
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        var route = notifications.GetOrAdd(notification.GetType(), static type =>
            (NotificationRoute)Activator.CreateInstance(typeof(NotificationRoute<>).MakeGenericType(type))!);
        return route.Publish(routes, publisher ?? SequentialPublisher.Instance, notification, cancellationToken);
    }

    private static class RequestCache<T>
    {
        internal static readonly ConcurrentDictionary<Type, RequestRoute<T>> Routes = new();
    }

    private abstract class RequestRoute<T>
    {
        public abstract Task<T> Send(IDispatchRoutes routes, IRequest<T> request, CancellationToken token);
    }

    private sealed class RequestRoute<TRequest, T> : RequestRoute<T> where TRequest : IRequest<T>
    {
        public override Task<T> Send(IDispatchRoutes routes, IRequest<T> request, CancellationToken token)
        {
            var typed = (TRequest)request;
            var pipeline = routes.Handler<TRequest, T>(typed, token);
            foreach (var behavior in routes.Behaviors<TRequest, T>().Reverse())
            {
                var next = pipeline;
                pipeline = () => behavior.Handle(typed, next, token);
            }
            return pipeline();
        }
    }

    private static readonly ConcurrentDictionary<Type, NotificationRoute> notifications = new();

    private abstract class NotificationRoute
    {
        public abstract Task Publish(IDispatchRoutes routes, INotificationPublisher publisher, INotification notification, CancellationToken token);
    }

    private sealed class NotificationRoute<T> : NotificationRoute where T : INotification
    {
        public override Task Publish(IDispatchRoutes routes, INotificationPublisher publisher, INotification notification, CancellationToken token)
        {
            return publisher.Publish(routes.Notifications<T>(), (T)notification, token);
        }
    }

    private sealed class SequentialPublisher : INotificationPublisher
    {
        internal static readonly SequentialPublisher Instance = new();
        public async Task Publish<T>(IEnumerable<INotificationHandler<T>> handlers, T notification, CancellationToken token)
            where T : INotification
        {
            foreach (var handler in handlers)
                await handler.Handle(notification, token).ConfigureAwait(false);
        }
    }
}
