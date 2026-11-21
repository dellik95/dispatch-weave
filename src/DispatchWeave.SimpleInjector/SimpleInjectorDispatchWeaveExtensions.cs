using DispatchWeave.Behaviors;
using DispatchWeave.Results;
using FluentValidation;
using SimpleInjector;

namespace DispatchWeave.SimpleInjector;

public static class SimpleInjectorDispatchWeaveExtensions
{
    public static Container AddDispatchWeave(
        this Container container,
        Action<DispatchWeaveBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(container);

        var registrar = new SimpleInjectorDispatchRegistrar(container);
        var builder = new DispatchWeaveBuilder(registrar);
        configure?.Invoke(builder);
        registrar.FinalizeRegistrations();

        var lifestyle = SimpleInjectorDispatchRegistrar.MapLifestyle(builder.DefaultLifetime, container);

        container.RegisterInstance<IServiceProvider>(new SimpleInjectorServiceProvider(container, registrar));
        container.Register<Func<Type, IEnumerable<object>>>(() => registrar.GetAllInstances, lifestyle);
        container.Register<IDispatchRoutes>(
            () => new ServiceProviderRoutes(((IServiceProvider)container).GetService, registrar.GetAllInstances, builder.Options),
            lifestyle);

        container.Register<IDispatcher>(() =>
        {
            var routes = container.GetInstance<IDispatchRoutes>();
            var publisher = ((IServiceProvider)container).GetService(typeof(INotificationPublisher)) as INotificationPublisher;
            return new Dispatcher(routes, publisher, builder.Options);
        }, lifestyle);
        container.Register<ISender>(() => container.GetInstance<IDispatcher>(), lifestyle);
        container.Register<IPublisher>(() => container.GetInstance<IDispatcher>(), lifestyle);

        return container;
    }
}

/// <summary>
/// <see cref="IDispatchRegistrar"/> adapter for SimpleInjector's <see cref="Container"/>.
/// </summary>
public sealed class SimpleInjectorDispatchRegistrar(Container container) : IDispatchRegistrar
{
    private readonly List<(Type ServiceType, Type ImplementationType, DispatchServiceLifetime Lifetime, bool Replace)> singleItems = [];
    private readonly List<(Type ServiceType, Type ImplementationType, DispatchServiceLifetime Lifetime)> collectionItems = [];
    private readonly List<(Type ServiceType, object Instance)> collectionInstances = [];
    private readonly Dictionary<Type, object> instances = [];

    public Container Container { get; } = container ?? throw new ArgumentNullException(nameof(container));

    public void RegisterInstance<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        instances[typeof(TService)] = instance;
    }

    public void Register(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped,
        bool replace = false)
    {
        singleItems.Add((serviceType, implementationType, lifetime, replace));
    }

    public void RegisterCollection(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped)
    {
        collectionItems.Add((serviceType, implementationType, lifetime));
    }

    public void RegisterCollectionInstance(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        collectionInstances.Add((serviceType, instance));
    }

    internal IEnumerable<object> GetAllInstances(Type serviceType)
    {
        try
        {
            return Container.GetAllInstances(serviceType);
        }
        catch (ActivationException)
        {
            var single = ((IServiceProvider)Container).GetService(serviceType);
            return single is not null ? [single] : [];
        }
    }

    internal void FinalizeRegistrations()
    {
        var registeredServices = new HashSet<Type>();

        foreach (var (serviceType, instance) in instances)
        {
            if (registeredServices.Add(serviceType))
                Container.RegisterInstance(serviceType, instance);
        }

        foreach (var group in singleItems.Distinct().GroupBy(static x => (x.ImplementationType, x.Lifetime)))
        {
            var lifestyle = MapLifestyle(group.Key.Lifetime, Container);
            var registration = lifestyle.CreateRegistration(group.Key.ImplementationType, Container);
            foreach (var item in group)
            {
                if (registeredServices.Add(item.ServiceType))
                {
                    Container.AddRegistration(item.ServiceType, registration);
                }
            }
        }

        var hasValidation = collectionItems.Any(static x =>
            x.ImplementationType == typeof(ValidationBehavior<,>) ||
            x.ImplementationType == typeof(ThrowingValidationBehavior<,>));

        var hasProcessors = collectionItems.Any(static x =>
            x.ImplementationType == typeof(RequestPreProcessorBehavior<,>) ||
            x.ImplementationType == typeof(RequestPostProcessorBehavior<,>));

        if (hasValidation || hasProcessors)
        {
            Container.ResolveUnregisteredType += (_, e) =>
            {
                if (!e.UnregisteredServiceType.IsGenericType ||
                    e.UnregisteredServiceType.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                {
                    return;
                }

                var itemType = e.UnregisteredServiceType.GetGenericArguments()[0];
                if (!itemType.IsGenericType)
                    return;

                var def = itemType.GetGenericTypeDefinition();
                var isValidator = hasValidation && def == typeof(IValidator<>);
                var isProcessor = hasProcessors && (def == typeof(IRequestPreProcessor<>) || def == typeof(IRequestPostProcessor<,>));

                if (isValidator || isProcessor)
                {
                    e.Register(() =>
                    {
                        var single = ((IServiceProvider)Container).GetService(itemType);
                        if (single is not null)
                        {
                            var arr = Array.CreateInstance(itemType, 1);
                            arr.SetValue(single, 0);
                            return arr;
                        }

                        return Array.CreateInstance(itemType, 0);
                    });
                }
            };
        }

        foreach (var (serviceType, implType, lifetime) in collectionItems.Distinct())
        {
            var lifestyle = MapLifestyle(lifetime, Container);
            Container.Collection.Append(serviceType, implType, lifestyle);
        }

        foreach (var (serviceType, instance) in collectionInstances)
        {
            Container.Collection.AppendInstance(serviceType, instance);
        }
    }

    internal static Lifestyle MapLifestyle(DispatchServiceLifetime lifetime, Container container) =>
        lifetime switch
        {
            DispatchServiceLifetime.Singleton => Lifestyle.Singleton,
            DispatchServiceLifetime.Scoped when container.Options.DefaultScopedLifestyle is not null =>
                container.Options.DefaultScopedLifestyle,
            _ => Lifestyle.Transient
        };
}

internal sealed class SimpleInjectorServiceProvider(Container container, SimpleInjectorDispatchRegistrar registrar) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var itemType = serviceType.GetGenericArguments()[0];
            var items = registrar.GetAllInstances(itemType).ToList();
            var arr = Array.CreateInstance(itemType, items.Count);
            for (var i = 0; i < items.Count; i++)
                arr.SetValue(items[i], i);
            return arr;
        }

        return ((IServiceProvider)container).GetService(serviceType);
    }
}
