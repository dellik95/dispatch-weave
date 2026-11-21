using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DispatchWeave.Registration;

/// <summary>
/// Built-in container-free <see cref="IDispatchRegistrar"/> that backs <see cref="DispatchWeaveBuilder"/>
/// when used standalone without an external dependency injection container.
/// </summary>
public sealed class StandaloneDispatchRegistrar : IDispatchRegistrar, IServiceProvider
{
    private readonly ConcurrentDictionary<Type, Func<object>> singleRegistrations = new();
    private readonly ConcurrentDictionary<Type, List<Func<object>>> collectionRegistrations = new();

    public HandlerRegistry Handlers { get; } = new();
    public PipelineRegistry Pipelines { get; } = new();
    public NotificationRegistry Notifications { get; } = new();

    public StandaloneDispatchRegistrar()
    {
        singleRegistrations[typeof(IServiceProvider)] = () => this;
        singleRegistrations[typeof(Func<Type, IEnumerable<object>>)] = () => (Func<Type, IEnumerable<object>>)ResolveCollectionObjects;
    }

    public void RegisterInstance<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        singleRegistrations[typeof(TService)] = () => instance;
        singleRegistrations[instance.GetType()] = () => instance;
    }

    public void Register(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped,
        bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);

        var factory = CreateFactory(implementationType, lifetime);

        if (replace)
            singleRegistrations[serviceType] = factory;
        else
            singleRegistrations.TryAdd(serviceType, factory);

        TryWireIntoRegistries(serviceType, factory);
    }

    public void RegisterCollection(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);

        if (serviceType == typeof(IPipelineBehavior<,>) && implementationType.IsGenericTypeDefinition)
        {
            Pipelines.AddOpenBehavior(implementationType, closedType => CreateInstance(closedType));
            return;
        }

        if (serviceType == typeof(IStreamPipelineBehavior<,>) && implementationType.IsGenericTypeDefinition)
        {
            Pipelines.AddOpenStreamBehavior(implementationType, closedType => CreateInstance(closedType));
            return;
        }

        var factory = CreateFactory(implementationType, lifetime);
        AddToCollection(serviceType, factory);
        TryWireIntoRegistries(serviceType, factory);
    }

    public void RegisterCollectionInstance(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);

        Func<object> factory = () => instance;
        AddToCollection(serviceType, factory);
        TryWireIntoRegistries(serviceType, factory);
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (singleRegistrations.TryGetValue(serviceType, out var factory))
            return factory();

        if (serviceType.IsGenericType)
        {
            var genericDef = serviceType.GetGenericTypeDefinition();

            if (genericDef == typeof(IEnumerable<>))
            {
                var itemType = serviceType.GetGenericArguments()[0];
                var items = ResolveCollectionObjects(itemType).ToList();
                var array = Array.CreateInstance(itemType, items.Count);
                for (var i = 0; i < items.Count; i++)
                    array.SetValue(items[i], i);
                return array;
            }

            if (genericDef == typeof(ILogger<>))
            {
                var categoryType = serviceType.GetGenericArguments()[0];
                if (singleRegistrations.TryGetValue(typeof(ILoggerFactory), out var lfFactory) &&
                    lfFactory() is ILoggerFactory loggerFactory)
                {
                    return typeof(LoggerFactoryExtensions)
                        .GetMethod(nameof(LoggerFactoryExtensions.CreateLogger), [typeof(ILoggerFactory)])!
                        .MakeGenericMethod(categoryType)
                        .Invoke(null, [loggerFactory]);
                }

                var nullLoggerType = typeof(NullLogger<>).MakeGenericType(categoryType);
                return nullLoggerType.GetProperty(nameof(NullLogger<object>.Instance), BindingFlags.Public | BindingFlags.Static)!
                    .GetValue(null);
            }
        }

        if (!serviceType.IsAbstract && !serviceType.IsInterface && !serviceType.IsGenericTypeDefinition)
            return CreateInstance(serviceType);

        return null;
    }

    private IEnumerable<object> ResolveCollectionObjects(Type itemType)
    {
        if (collectionRegistrations.TryGetValue(itemType, out var list))
        {
            List<Func<object>> snapshot;
            lock (list)
            {
                snapshot = [.. list];
            }

            return snapshot.Select(static f => f());
        }

        if (singleRegistrations.TryGetValue(itemType, out var single))
            return [single()];

        return [];
    }

    private void AddToCollection(Type serviceType, Func<object> factory)
    {
        var list = collectionRegistrations.GetOrAdd(serviceType, static _ => []);
        lock (list)
        {
            list.Add(factory);
        }
    }

    private void TryWireIntoRegistries(Type serviceType, Func<object> factory)
    {
        if (!serviceType.IsGenericType)
            return;

        var genericDef = serviceType.GetGenericTypeDefinition();
        var args = serviceType.GetGenericArguments();

        if (genericDef == typeof(IRequestHandler<,>))
        {
            Handlers.Register(args[0], args[1], factory);
        }
        else if (genericDef == typeof(IStreamRequestHandler<,>))
        {
            Handlers.RegisterStream(args[0], args[1], factory);
        }
        else if (genericDef == typeof(INotificationHandler<>))
        {
            Notifications.AddHandler(args[0], factory);
        }
        else if (genericDef == typeof(IPipelineBehavior<,>))
        {
            Pipelines.AddBehavior(args[0], args[1], factory);
        }
        else if (genericDef == typeof(IStreamPipelineBehavior<,>))
        {
            Pipelines.AddStreamBehavior(args[0], args[1], factory);
        }
    }

    private Func<object> CreateFactory(Type implementationType, DispatchServiceLifetime lifetime)
    {
        if (lifetime == DispatchServiceLifetime.Singleton)
        {
            var lazy = new Lazy<object>(() => CreateInstance(implementationType), LazyThreadSafetyMode.ExecutionAndPublication);
            return () => lazy.Value;
        }

        return () => CreateInstance(implementationType);
    }

    private object CreateInstance(Type implementationType)
    {
        var constructors = implementationType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(static c => c.GetParameters().Length)
            .ToArray();

        foreach (var ctor in constructors)
        {
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            var matched = true;

            for (var i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var resolved = GetService(param.ParameterType);
                if (resolved is not null)
                {
                    args[i] = resolved;
                }
                else if (param.HasDefaultValue)
                {
                    args[i] = param.DefaultValue;
                }
                else
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return ctor.Invoke(args);
        }

        return Activator.CreateInstance(implementationType)!;
    }
}
