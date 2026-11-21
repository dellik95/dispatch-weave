using Autofac;
using Autofac.Builder;

namespace DispatchWeave.Autofac;

public static class AutofacDispatchWeaveExtensions
{
    public static ContainerBuilder RegisterDispatchWeave(
        this ContainerBuilder containerBuilder,
        Action<DispatchWeaveBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(containerBuilder);

        var registrar = new AutofacDispatchRegistrar(containerBuilder);
        var weaveBuilder = new DispatchWeaveBuilder(registrar);
        configure?.Invoke(weaveBuilder);

        containerBuilder.Register<Func<Type, IEnumerable<object>>>(ctx =>
        {
            var scope = ctx.Resolve<ILifetimeScope>();
            return serviceType =>
            {
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
                return (scope.ResolveOptional(enumerableType) as IEnumerable<object>) ?? [];
            };
        }).InstancePerLifetimeScope();

        containerBuilder.Register<IServiceProvider>(ctx => new AutofacServiceProvider(ctx.Resolve<ILifetimeScope>()))
            .InstancePerLifetimeScope()
            .PreserveExistingDefaults();

        containerBuilder.Register<IDispatchRoutes>(ctx =>
        {
            var scope = ctx.Resolve<ILifetimeScope>();
            var options = ctx.Resolve<DispatchWeaveOptions>();
            var getMany = ctx.Resolve<Func<Type, IEnumerable<object>>>();
            return new ServiceProviderRoutes(scope.ResolveOptional, getMany, options);
        }).InstancePerLifetimeScope();

        containerBuilder.Register(ctx =>
        {
            var routes = ctx.Resolve<IDispatchRoutes>();
            var options = ctx.Resolve<DispatchWeaveOptions>();
            var publisher = ctx.ResolveOptional<INotificationPublisher>();
            return new Dispatcher(routes, publisher, options);
        })
        .As<IDispatcher>()
        .As<ISender>()
        .As<IPublisher>()
        .InstancePerLifetimeScope();

        return containerBuilder;
    }
}

/// <summary>
/// <see cref="IDispatchRegistrar"/> adapter for Autofac's <see cref="ContainerBuilder"/>.
/// </summary>
public sealed class AutofacDispatchRegistrar(ContainerBuilder builder) : IDispatchRegistrar
{
    public ContainerBuilder Builder { get; } = builder ?? throw new ArgumentNullException(nameof(builder));

    public void RegisterInstance<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        Builder.RegisterInstance(instance).As<TService>().AsSelf().SingleInstance();
    }

    public void Register(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped,
        bool replace = false)
    {
        var registration = Builder.RegisterType(implementationType).As(serviceType);
        ApplyLifetime(registration, lifetime);
        if (!replace)
            registration.PreserveExistingDefaults();
    }

    public void RegisterCollection(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped)
    {
        if (implementationType.IsGenericTypeDefinition)
        {
            var genericReg = Builder.RegisterGeneric(implementationType).As(serviceType);
            ApplyLifetime(genericReg, lifetime);
            return;
        }

        var registration = Builder.RegisterType(implementationType).As(serviceType);
        ApplyLifetime(registration, lifetime);
    }

    public void RegisterCollectionInstance(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        Builder.RegisterInstance(instance).As(serviceType).SingleInstance();
    }

    private static void ApplyLifetime<TLimit, TActivatorData, TRegistrationStyle>(
        IRegistrationBuilder<TLimit, TActivatorData, TRegistrationStyle> registration,
        DispatchServiceLifetime lifetime)
    {
        switch (lifetime)
        {
            case DispatchServiceLifetime.Transient:
                registration.InstancePerDependency();
                break;
            case DispatchServiceLifetime.Singleton:
                registration.SingleInstance();
                break;
            default:
                registration.InstancePerLifetimeScope();
                break;
        }
    }
}

internal sealed class AutofacServiceProvider(ILifetimeScope scope) : IServiceProvider
{
    public object? GetService(Type serviceType) => scope.ResolveOptional(serviceType);
}
