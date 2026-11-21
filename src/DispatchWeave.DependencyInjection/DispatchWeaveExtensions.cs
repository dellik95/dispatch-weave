using System.Reflection;
using DispatchWeave.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DispatchWeave;

public static class DispatchWeaveExtensions
{
    public static IServiceCollection AddDispatchWeave(this IServiceCollection services, Action<DispatchWeaveBuilder> configure)
    {
        services.TryAddScoped<IDispatchRoutes, ServiceProviderRoutes>();
        services.TryAddScoped<IDispatcher, Dispatcher>();
        configure(new DispatchWeaveBuilder(services));
        return services;
    }
}

public sealed class DispatchWeaveBuilder(IServiceCollection services)
{
    public DispatchWeaveBuilder RegisterHandlers(params Assembly[] assemblies)
    {
        foreach (var implementation in assemblies.Distinct().SelectMany(a => a.DefinedTypes)
                     .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters))
        foreach (var contract in implementation.ImplementedInterfaces.Where(i => i.IsGenericType))
        {
            var definition = contract.GetGenericTypeDefinition();
            if (definition == typeof(IRequestHandler<,>) || definition == typeof(INotificationHandler<>))
                services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, implementation));
        }
        return this;
    }

    public DispatchWeaveBuilder AddBehavior(Type openBehavior)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), openBehavior));
        return this;
    }

    public DispatchWeaveBuilder WithValidation() => AddBehavior(typeof(ValidationBehavior<,>));
}

internal sealed class ServiceProviderRoutes(IServiceProvider provider) : IDispatchRoutes
{
    public RequestHandlerDelegate<TResponse> Handler<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IRequest<TResponse> =>
        () => (provider.GetService<IRequestHandler<TRequest, TResponse>>() ??
            throw HandlerResolutionException.Missing(typeof(TRequest))).Handle(request, token);

    public IEnumerable<IPipelineBehavior<TRequest, TResponse>> Behaviors<TRequest, TResponse>()
        where TRequest : IRequest<TResponse> => provider.GetServices<IPipelineBehavior<TRequest, TResponse>>();

    public IEnumerable<INotificationHandler<TNotification>> Notifications<TNotification>() where TNotification : INotification =>
        provider.GetServices<INotificationHandler<TNotification>>();
}
