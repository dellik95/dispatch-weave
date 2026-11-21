using System.Collections.Concurrent;

namespace DispatchWeave;

/// <summary>
/// Universal <see cref="IDispatchRoutes"/> adapter backed by an <see cref="IServiceProvider"/> or resolution delegates.
/// Used across all DI container integrations (Microsoft.Extensions.DependencyInjection, Autofac, SimpleInjector, and custom containers).
/// </summary>
public sealed class ServiceProviderRoutes : IDispatchRoutes
{
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), Type[]> InheritedBehaviorContracts = new();
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), Type[]> InheritedStreamBehaviorContracts = new();

    private readonly Func<Type, object?> getSingle;
    private readonly Func<Type, IEnumerable<object>> getMany;
    private readonly DispatchWeaveOptions options;

    public ServiceProviderRoutes(IServiceProvider provider, DispatchWeaveOptions? options = null)
        : this(
            (provider ?? throw new ArgumentNullException(nameof(provider))).GetService,
            serviceType =>
            {
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
                return (provider.GetService(enumerableType) as IEnumerable<object>) ?? [];
            },
            options)
    {
    }

    public ServiceProviderRoutes(Func<Type, object?> serviceFactory, DispatchWeaveOptions? options = null)
        : this(
            serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory)),
            serviceType =>
            {
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
                if (serviceFactory(enumerableType) is IEnumerable<object> items)
                    return items;
                if (serviceFactory(serviceType) is { } single)
                    return [single];
                return [];
            },
            options)
    {
    }

    public ServiceProviderRoutes(
        Func<Type, object?> getSingle,
        Func<Type, IEnumerable<object>> getMany,
        DispatchWeaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(getSingle);
        ArgumentNullException.ThrowIfNull(getMany);
        this.getSingle = getSingle;
        this.getMany = getMany;
        this.options = options ?? (getSingle(typeof(DispatchWeaveOptions)) as DispatchWeaveOptions) ?? new DispatchWeaveOptions();
    }

    public RequestHandlerDelegate<TResponse> Handler<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IRequest<TResponse> =>
        () =>
        {
            var handler = getSingle(typeof(IRequestHandler<TRequest, TResponse>)) as IRequestHandler<TRequest, TResponse>
                ?? throw HandlerResolutionException.Missing(typeof(TRequest));
            return handler.Handle(request, token);
        };

    public IEnumerable<IPipelineBehavior<TRequest, TResponse>> Behaviors<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var direct = ResolveEnumerable<IPipelineBehavior<TRequest, TResponse>>(
            typeof(IPipelineBehavior<TRequest, TResponse>));

        if (!options.IncludeInheritedBehaviors)
            return direct;

        var inheritedContracts = InheritedBehaviorContracts.GetOrAdd(
            (typeof(TRequest), typeof(TResponse)),
            static key => BuildInheritedBehaviorContracts(key.RequestType, key.ResponseType, typeof(IRequest<>), typeof(IPipelineBehavior<,>)));

        if (inheritedContracts.Length == 0)
            return direct;

        return CombineWithInherited(direct, inheritedContracts);
    }

    public IEnumerable<INotificationHandler<TNotification>> Notifications<TNotification>()
        where TNotification : INotification =>
        ResolveEnumerable<INotificationHandler<TNotification>>(typeof(INotificationHandler<TNotification>));

    public StreamHandlerDelegate<TResponse> StreamHandler<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IStreamRequest<TResponse> =>
        () =>
        {
            var handler = getSingle(typeof(IStreamRequestHandler<TRequest, TResponse>)) as IStreamRequestHandler<TRequest, TResponse>
                ?? throw HandlerResolutionException.Missing(typeof(TRequest));
            return handler.Handle(request, token);
        };

    public IEnumerable<IStreamPipelineBehavior<TRequest, TResponse>> StreamBehaviors<TRequest, TResponse>()
        where TRequest : IStreamRequest<TResponse>
    {
        var direct = ResolveEnumerable<IStreamPipelineBehavior<TRequest, TResponse>>(
            typeof(IStreamPipelineBehavior<TRequest, TResponse>));

        if (!options.IncludeInheritedBehaviors)
            return direct;

        var inheritedContracts = InheritedStreamBehaviorContracts.GetOrAdd(
            (typeof(TRequest), typeof(TResponse)),
            static key => BuildInheritedBehaviorContracts(key.RequestType, key.ResponseType, typeof(IStreamRequest<>), typeof(IStreamPipelineBehavior<,>)));

        if (inheritedContracts.Length == 0)
            return direct;

        return CombineWithInherited(direct, inheritedContracts);
    }

    private IEnumerable<TService> ResolveEnumerable<TService>(Type serviceType)
        where TService : class =>
        getMany(serviceType).OfType<TService>();

    private IEnumerable<TBehavior> CombineWithInherited<TBehavior>(
        IEnumerable<TBehavior> direct,
        Type[] inheritedBehaviorContracts)
        where TBehavior : class
    {
        var directList = direct.ToList();
        var seenTypes = new HashSet<Type>(directList.Select(static b => b.GetType()));

        foreach (var contractType in inheritedBehaviorContracts)
        {
            IEnumerable<TBehavior> inherited;
            try
            {
                inherited = ResolveEnumerable<TBehavior>(contractType);
            }
            catch
            {
                continue;
            }

            foreach (var behavior in inherited)
            {
                var behaviorType = behavior.GetType();
                var keyType = behaviorType.IsGenericType ? behaviorType.GetGenericTypeDefinition() : behaviorType;
                if (seenTypes.Any(existing => (existing.IsGenericType ? existing.GetGenericTypeDefinition() : existing) == keyType))
                    continue;

                seenTypes.Add(behaviorType);
                directList.Add(behavior);
            }
        }

        return directList;
    }

    private static Type[] BuildInheritedBehaviorContracts(
        Type requestType,
        Type responseType,
        Type openRequestInterface,
        Type openBehaviorInterface)
    {
        var closedRequestContract = openRequestInterface.MakeGenericType(responseType);
        List<Type> contracts = [];

        for (var baseType = requestType.BaseType;
             baseType is not null && baseType != typeof(object) && closedRequestContract.IsAssignableFrom(baseType);
             baseType = baseType.BaseType)
        {
            contracts.Add(openBehaviorInterface.MakeGenericType(baseType, responseType));
        }

        foreach (var iface in requestType.GetInterfaces())
        {
            if (iface != closedRequestContract && closedRequestContract.IsAssignableFrom(iface))
            {
                contracts.Add(openBehaviorInterface.MakeGenericType(iface, responseType));
            }
        }

        return [.. contracts];
    }
}
