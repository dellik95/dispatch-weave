using System.Collections.Concurrent;
using DispatchWeave.Registration;

namespace DispatchWeave;

/// <summary>
/// Default production implementation of <see cref="IDispatcher"/> supporting requests, streams, and polymorphic notifications.
/// </summary>
public sealed class Dispatcher : IDispatcher
{
    private readonly IDispatchRoutes routes;
    private readonly INotificationPublisher publisher;
    private readonly DispatchWeaveOptions options;

    public Dispatcher(
        IDispatchRoutes routes,
        INotificationPublisher? publisher = null,
        DispatchWeaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        this.routes = routes;
        this.options = options ?? (routes as RegistryRoutes)?.Options ?? new DispatchWeaveOptions();
        this.publisher = this.options.ResolvePublisher(publisher);
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = RequestCache<TResponse>.Routes.GetOrAdd(request.GetType(), static type =>
            (RequestRoute<TResponse>)Activator.CreateInstance(typeof(RequestRoute<,>).MakeGenericType(type, typeof(TResponse)))!);
        return route.Send(routes, options, request, cancellationToken);
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = StreamCache<TResponse>.Routes.GetOrAdd(request.GetType(), static type =>
            (StreamRoute<TResponse>)Activator.CreateInstance(typeof(StreamRoute<,>).MakeGenericType(type, typeof(TResponse)))!);
        return route.CreateStream(routes, options, request, cancellationToken);
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        var route = NotificationCache.Routes.GetOrAdd(notification.GetType(), static type =>
            (NotificationRoute)Activator.CreateInstance(typeof(NotificationRoute<>).MakeGenericType(type))!);
        return route.Publish(routes, publisher, options, notification, cancellationToken);
    }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification is not INotification typed)
            throw HandlerResolutionException.InvalidNotification(notification.GetType());

        return Publish(typed, cancellationToken);
    }

    private static class RequestCache<TResponse>
    {
        internal static readonly ConcurrentDictionary<Type, RequestRoute<TResponse>> Routes = new();
    }

    private abstract class RequestRoute<TResponse>
    {
        public abstract Task<TResponse> Send(
            IDispatchRoutes routes,
            DispatchWeaveOptions options,
            IRequest<TResponse> request,
            CancellationToken token);
    }

    private sealed class RequestRoute<TRequest, TResponse> : RequestRoute<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Send(
            IDispatchRoutes routes,
            DispatchWeaveOptions options,
            IRequest<TResponse> request,
            CancellationToken token)
        {
            var typed = (TRequest)request;
            var pipeline = routes.Handler<TRequest, TResponse>(typed, token);
            var reverseRegistrationOrder = options.LastBehaviorOutermost &&
                routes is not RegistryRoutes { LastBehaviorOutermost: true };

            var behaviors = PipelineOrderResolver.MaterializeOrdered(
                routes.Behaviors<TRequest, TResponse>(),
                options.EnablePipelineOrdering,
                reverseRegistrationOrder);

            for (var i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var next = pipeline;
                pipeline = () => behavior.Handle(typed, next, token);
            }

            return pipeline();
        }
    }

    private static class StreamCache<TResponse>
    {
        internal static readonly ConcurrentDictionary<Type, StreamRoute<TResponse>> Routes = new();
    }

    private abstract class StreamRoute<TResponse>
    {
        public abstract IAsyncEnumerable<TResponse> CreateStream(
            IDispatchRoutes routes,
            DispatchWeaveOptions options,
            IStreamRequest<TResponse> request,
            CancellationToken token);
    }

    private sealed class StreamRoute<TRequest, TResponse> : StreamRoute<TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public override IAsyncEnumerable<TResponse> CreateStream(
            IDispatchRoutes routes,
            DispatchWeaveOptions options,
            IStreamRequest<TResponse> request,
            CancellationToken token)
        {
            var typed = (TRequest)request;
            var pipeline = routes.StreamHandler<TRequest, TResponse>(typed, token);
            var reverseRegistrationOrder = options.LastBehaviorOutermost &&
                routes is not RegistryRoutes { LastBehaviorOutermost: true };

            var behaviors = PipelineOrderResolver.MaterializeOrdered(
                routes.StreamBehaviors<TRequest, TResponse>(),
                options.EnablePipelineOrdering,
                reverseRegistrationOrder);

            for (var i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var next = pipeline;
                pipeline = () => behavior.Handle(typed, next, token);
            }

            return pipeline();
        }
    }

    private static class NotificationCache
    {
        internal static readonly ConcurrentDictionary<Type, NotificationRoute> Routes = new();
    }

    private abstract class NotificationRoute
    {
        public abstract Task Publish(
            IDispatchRoutes routes,
            INotificationPublisher publisher,
            DispatchWeaveOptions options,
            INotification notification,
            CancellationToken token);
    }

    private sealed class NotificationRoute<TConcrete> : NotificationRoute
        where TConcrete : INotification
    {
        private static readonly IPolymorphicSource<TConcrete>[] PolymorphicSources = BuildPolymorphicSources();

        public override Task Publish(
            IDispatchRoutes routes,
            INotificationPublisher publisher,
            DispatchWeaveOptions options,
            INotification notification,
            CancellationToken token)
        {
            var typed = (TConcrete)notification;

            if (!options.PolymorphicNotificationDispatch || PolymorphicSources.Length <= 1)
            {
                return publisher.Publish(routes.Notifications<TConcrete>(), typed, token);
            }

            var handlers = ResolvePolymorphicHandlers(routes);
            return publisher.Publish(handlers, typed, token);
        }

        private static List<INotificationHandler<TConcrete>> ResolvePolymorphicHandlers(IDispatchRoutes routes)
        {
            List<INotificationHandler<TConcrete>> result = [];
            HashSet<object> seenInstances = new(ReferenceEqualityComparer.Instance);
            HashSet<Type> seenTypesFromPriorSources = [];
            List<Type> currentSourceTypes = [];

            for (var i = 0; i < PolymorphicSources.Length; i++)
            {
                currentSourceTypes.Clear();
                foreach (var (handler, rawInstance) in PolymorphicSources[i].Resolve(routes))
                {
                    var handlerType = rawInstance.GetType();
                    if (seenTypesFromPriorSources.Contains(handlerType))
                        continue;

                    if (seenInstances.Add(rawInstance))
                    {
                        result.Add(handler);
                        currentSourceTypes.Add(handlerType);
                    }
                }

                for (var j = 0; j < currentSourceTypes.Count; j++)
                {
                    seenTypesFromPriorSources.Add(currentSourceTypes[j]);
                }
            }

            return result;
        }

        private static IPolymorphicSource<TConcrete>[] BuildPolymorphicSources()
        {
            var concreteType = typeof(TConcrete);
            List<Type> contractTypes = [concreteType];

            for (var baseType = concreteType.BaseType;
                 baseType is not null && typeof(INotification).IsAssignableFrom(baseType);
                 baseType = baseType.BaseType)
            {
                contractTypes.Add(baseType);
            }

            foreach (var iface in concreteType.GetInterfaces())
            {
                if (typeof(INotification).IsAssignableFrom(iface) && !contractTypes.Contains(iface))
                {
                    contractTypes.Add(iface);
                }
            }

            var sources = new IPolymorphicSource<TConcrete>[contractTypes.Count];
            for (var i = 0; i < contractTypes.Count; i++)
            {
                var sourceType = typeof(PolymorphicSource<,>).MakeGenericType(concreteType, contractTypes[i]);
                sources[i] = (IPolymorphicSource<TConcrete>)Activator.CreateInstance(sourceType)!;
            }

            return sources;
        }
    }

    private interface IPolymorphicSource<TConcrete> where TConcrete : INotification
    {
        IEnumerable<(INotificationHandler<TConcrete> Handler, object RawInstance)> Resolve(IDispatchRoutes routes);
    }

    private sealed class PolymorphicSource<TConcrete, TContract> : IPolymorphicSource<TConcrete>
        where TConcrete : TContract, INotification
        where TContract : INotification
    {
        public IEnumerable<(INotificationHandler<TConcrete> Handler, object RawInstance)> Resolve(IDispatchRoutes routes)
        {
            foreach (var handler in routes.Notifications<TContract>())
            {
                if (handler is INotificationHandler<TConcrete> direct)
                {
                    yield return (direct, handler);
                }
                else
                {
                    yield return (new StructNotificationHandlerAdapter<TConcrete, TContract>(handler), handler);
                }
            }
        }
    }

    private sealed class StructNotificationHandlerAdapter<TConcrete, TContract>(INotificationHandler<TContract> inner)
        : INotificationHandler<TConcrete>
        where TConcrete : TContract, INotification
        where TContract : INotification
    {
        public Task Handle(TConcrete notification, CancellationToken cancellationToken = default) =>
            inner.Handle(notification, cancellationToken);
    }
}
