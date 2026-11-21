using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DispatchWeave;

public static class DispatchWeaveExtensions
{
    public static IServiceCollection AddDispatchWeave(
        this IServiceCollection services,
        Action<DispatchWeaveBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existingOptions = services
            .FirstOrDefault(static d => d.ServiceType == typeof(DispatchWeaveOptions))
            ?.ImplementationInstance as DispatchWeaveOptions;

        var options = existingOptions ?? new DispatchWeaveOptions();
        var registrar = new ServiceCollectionRegistrar(services);
        var builder = new DispatchWeaveBuilder(registrar, options);
        configure?.Invoke(builder);

        var lifetime = ServiceCollectionRegistrar.MapLifetime(builder.DefaultLifetime);
        services.TryAdd(ServiceDescriptor.Describe(typeof(IDispatchRoutes), typeof(ServiceProviderRoutes), lifetime));
        services.TryAdd(ServiceDescriptor.Describe(typeof(IDispatcher), typeof(Dispatcher), lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(ISender),
            static sp => sp.GetRequiredService<IDispatcher>(),
            lifetime));
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(IPublisher),
            static sp => sp.GetRequiredService<IDispatcher>(),
            lifetime));

        return services;
    }
}

/// <summary>
/// <see cref="IDispatchRegistrar"/> adapter for <see cref="IServiceCollection"/> (Microsoft.Extensions.DependencyInjection).
/// </summary>
public sealed class ServiceCollectionRegistrar(IServiceCollection services) : IDispatchRegistrar
{
    public IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));

    public void RegisterInstance<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        Services.Replace(ServiceDescriptor.Singleton(typeof(TService), instance));
    }

    public void Register(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped,
        bool replace = false)
    {
        var descriptor = ServiceDescriptor.Describe(serviceType, implementationType, MapLifetime(lifetime));
        if (replace)
            Services.Replace(descriptor);
        else
            Services.TryAddEnumerable(descriptor);
    }

    public void RegisterCollection(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped)
    {
        Services.TryAddEnumerable(ServiceDescriptor.Describe(serviceType, implementationType, MapLifetime(lifetime)));
    }

    public void RegisterCollectionInstance(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton(serviceType, instance));
    }

    internal static ServiceLifetime MapLifetime(DispatchServiceLifetime lifetime) =>
        lifetime switch
        {
            DispatchServiceLifetime.Transient => ServiceLifetime.Transient,
            DispatchServiceLifetime.Singleton => ServiceLifetime.Singleton,
            _ => ServiceLifetime.Scoped
        };
}
