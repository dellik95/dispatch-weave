using System.Reflection;
using DispatchWeave.Behaviors;
using DispatchWeave.Registration;
using DispatchWeave.Results;
using FluentValidation;

namespace DispatchWeave;

/// <summary>
/// Container-agnostic fluent builder for configuring DispatchWeave handlers, pipelines, behaviors, and options.
/// Can be used with any DI container (via <see cref="IDispatchRegistrar"/>) or completely standalone via <see cref="Create"/> and <see cref="Build"/>.
/// </summary>
public sealed class DispatchWeaveBuilder
{
    public IDispatchRegistrar Registrar { get; }
    public DispatchWeaveOptions Options { get; }
    public DispatchServiceLifetime DefaultLifetime { get; private set; } = DispatchServiceLifetime.Scoped;

    public DispatchWeaveBuilder()
        : this(new StandaloneDispatchRegistrar(), new DispatchWeaveOptions())
    {
    }

    public DispatchWeaveBuilder(IDispatchRegistrar registrar, DispatchWeaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        Registrar = registrar;
        Options = options ?? new DispatchWeaveOptions();
        Registrar.RegisterInstance(Options);
    }

    /// <summary>
    /// Creates a new standalone <see cref="DispatchWeaveBuilder"/> that does not require an external DI container.
    /// </summary>
    public static DispatchWeaveBuilder Create(Action<DispatchWeaveOptions>? configureOptions = null)
    {
        var options = new DispatchWeaveOptions();
        configureOptions?.Invoke(options);
        return new DispatchWeaveBuilder(new StandaloneDispatchRegistrar(), options);
    }

    public DispatchWeaveBuilder Configure(Action<DispatchWeaveOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options);
        return this;
    }

    public DispatchWeaveBuilder Apply(Action<DispatchWeaveBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(this);
        return this;
    }

    public DispatchWeaveBuilder WithLifetime(DispatchServiceLifetime lifetime)
    {
        DefaultLifetime = lifetime;
        return this;
    }

    public DispatchWeaveBuilder RegisterHandlers(params Assembly[] assemblies) =>
        RegisterHandlers(filter: null, DefaultLifetime, assemblies);

    public DispatchWeaveBuilder RegisterHandlers(Func<Type, bool>? filter, params Assembly[] assemblies) =>
        RegisterHandlers(filter, DefaultLifetime, assemblies);

    public DispatchWeaveBuilder RegisterHandlers(
        Func<Type, bool>? filter,
        DispatchServiceLifetime lifetime,
        params Assembly[] assemblies)
    {
        foreach (var registration in Registration.AssemblyScanner.Scan(assemblies, filter))
        {
            switch (registration.Kind)
            {
                case DiscoveredContractKind.RequestHandler:
                case DiscoveredContractKind.StreamRequestHandler:
                    Registrar.Register(registration.ServiceType, registration.ImplementationType, lifetime);
                    break;
                default:
                    Registrar.RegisterCollection(registration.ServiceType, registration.ImplementationType, lifetime);
                    break;
            }
        }

        return this;
    }

    public DispatchWeaveBuilder RegisterHandlersFromAssemblyContaining<T>(
        Func<Type, bool>? filter = null,
        DispatchServiceLifetime? lifetime = null) =>
        RegisterHandlers(filter, lifetime ?? DefaultLifetime, typeof(T).Assembly);

    public DispatchWeaveBuilder RegisterHandler<TRequest, TResponse>(IRequestHandler<TRequest, TResponse> handler)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(handler);
        Registrar.RegisterInstance(handler);
        if (Registrar is StandaloneDispatchRegistrar standalone)
            standalone.Handlers.Register(handler);
        return this;
    }

    public DispatchWeaveBuilder RegisterHandler<TRequest>(IRequestHandler<TRequest> handler)
        where TRequest : IRequest<Unit>
    {
        ArgumentNullException.ThrowIfNull(handler);
        Registrar.RegisterInstance<IRequestHandler<TRequest, Unit>>(handler);
        if (Registrar is StandaloneDispatchRegistrar standalone)
            standalone.Handlers.Register(handler);
        return this;
    }

    public DispatchWeaveBuilder RegisterStreamHandler<TRequest, TResponse>(IStreamRequestHandler<TRequest, TResponse> handler)
        where TRequest : IStreamRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(handler);
        Registrar.RegisterInstance(handler);
        if (Registrar is StandaloneDispatchRegistrar standalone)
            standalone.Handlers.RegisterStream(handler);
        return this;
    }

    public DispatchWeaveBuilder AddNotificationHandler<TNotification>(INotificationHandler<TNotification> handler)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(handler);
        Registrar.RegisterCollectionInstance(typeof(INotificationHandler<TNotification>), handler);
        return this;
    }

    public DispatchWeaveBuilder AddValidator<TRequest>(IValidator<TRequest> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        Registrar.RegisterCollectionInstance(typeof(IValidator<TRequest>), validator);
        return this;
    }

    public DispatchWeaveBuilder AddValidator<TRequest, TValidator>(DispatchServiceLifetime? lifetime = null)
        where TValidator : class, IValidator<TRequest>
    {
        Registrar.RegisterCollection(typeof(IValidator<TRequest>), typeof(TValidator), lifetime ?? DefaultLifetime);
        return this;
    }

    public DispatchWeaveBuilder AddService<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        Registrar.RegisterInstance(instance);
        return this;
    }

    public DispatchWeaveBuilder AddService<TService, TImplementation>(DispatchServiceLifetime? lifetime = null)
        where TService : class
        where TImplementation : class, TService
    {
        Registrar.Register(typeof(TService), typeof(TImplementation), lifetime ?? DefaultLifetime);
        return this;
    }

    public DispatchWeaveBuilder AddBehavior(Type openOrClosedBehavior, DispatchServiceLifetime? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(openOrClosedBehavior);
        var effectiveLifetime = lifetime ?? DefaultLifetime;

        if (openOrClosedBehavior.IsGenericTypeDefinition)
        {
            Registrar.RegisterCollection(typeof(IPipelineBehavior<,>), openOrClosedBehavior, effectiveLifetime);
            return this;
        }

        foreach (var iface in openOrClosedBehavior.GetInterfaces()
                     .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
        {
            Registrar.RegisterCollection(iface, openOrClosedBehavior, effectiveLifetime);
        }

        return this;
    }

    public DispatchWeaveBuilder AddBehavior<TBehavior>(DispatchServiceLifetime? lifetime = null)
        where TBehavior : class =>
        AddBehavior(typeof(TBehavior), lifetime);

    public DispatchWeaveBuilder AddBehavior<TRequest, TResponse>(IPipelineBehavior<TRequest, TResponse> behavior)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(behavior);
        Registrar.RegisterCollectionInstance(typeof(IPipelineBehavior<TRequest, TResponse>), behavior);
        return this;
    }

    public DispatchWeaveBuilder AddBehavior<TRequest, TResponse, TBehavior>(DispatchServiceLifetime? lifetime = null)
        where TRequest : IRequest<TResponse>
        where TBehavior : class, IPipelineBehavior<TRequest, TResponse>
    {
        Registrar.RegisterCollection(
            typeof(IPipelineBehavior<TRequest, TResponse>),
            typeof(TBehavior),
            lifetime ?? DefaultLifetime);
        return this;
    }

    public DispatchWeaveBuilder AddStreamBehavior(Type openOrClosedStreamBehavior, DispatchServiceLifetime? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(openOrClosedStreamBehavior);
        var effectiveLifetime = lifetime ?? DefaultLifetime;

        if (openOrClosedStreamBehavior.IsGenericTypeDefinition)
        {
            Registrar.RegisterCollection(typeof(IStreamPipelineBehavior<,>), openOrClosedStreamBehavior, effectiveLifetime);
            return this;
        }

        foreach (var iface in openOrClosedStreamBehavior.GetInterfaces()
                     .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamPipelineBehavior<,>)))
        {
            Registrar.RegisterCollection(iface, openOrClosedStreamBehavior, effectiveLifetime);
        }

        return this;
    }

    public DispatchWeaveBuilder AddStreamBehavior<TBehavior>(DispatchServiceLifetime? lifetime = null)
        where TBehavior : class =>
        AddStreamBehavior(typeof(TBehavior), lifetime);

    public DispatchWeaveBuilder AddStreamBehavior<TRequest, TResponse>(IStreamPipelineBehavior<TRequest, TResponse> behavior)
        where TRequest : IStreamRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(behavior);
        Registrar.RegisterCollectionInstance(typeof(IStreamPipelineBehavior<TRequest, TResponse>), behavior);
        return this;
    }

    public DispatchWeaveBuilder AddStreamBehavior<TRequest, TResponse, TBehavior>(DispatchServiceLifetime? lifetime = null)
        where TRequest : IStreamRequest<TResponse>
        where TBehavior : class, IStreamPipelineBehavior<TRequest, TResponse>
    {
        Registrar.RegisterCollection(
            typeof(IStreamPipelineBehavior<TRequest, TResponse>),
            typeof(TBehavior),
            lifetime ?? DefaultLifetime);
        return this;
    }

    public DispatchWeaveBuilder WithValidation() =>
        AddBehavior(typeof(ValidationBehavior<,>));

    public DispatchWeaveBuilder WithThrowingValidation() =>
        AddBehavior(typeof(ThrowingValidationBehavior<,>));

    public DispatchWeaveBuilder WithPreAndPostProcessors()
    {
        AddBehavior(typeof(RequestPreProcessorBehavior<,>));
        AddBehavior(typeof(RequestPostProcessorBehavior<,>));
        return this;
    }

    public DispatchWeaveBuilder WithExceptionHandling()
    {
        AddBehavior(typeof(RequestExceptionHandlerBehavior<,>));
        AddBehavior(typeof(RequestExceptionActionBehavior<,>));
        return this;
    }

    public DispatchWeaveBuilder WithLogging() =>
        AddBehavior(typeof(LoggingBehavior<,>));

    public DispatchWeaveBuilder WithTelemetry()
    {
        AddBehavior(typeof(TelemetryBehavior<,>));
        AddStreamBehavior(typeof(StreamTelemetryBehavior<,>));
        return this;
    }

    public DispatchWeaveBuilder WithCaching(IDispatchCache? cache = null)
    {
        Registrar.RegisterInstance<IDispatchCache>(cache ?? new InMemoryDispatchCache());
        return AddBehavior(typeof(CachingBehavior<,>));
    }

    public DispatchWeaveBuilder WithTimeout(TimeSpan? defaultTimeout = null)
    {
        if (defaultTimeout is not null)
            Options.DefaultTimeout = defaultTimeout;

        return AddBehavior(typeof(TimeoutBehavior<,>));
    }

    public DispatchWeaveBuilder WithRetry(int defaultMaxRetries = 0, TimeSpan? defaultDelay = null)
    {
        if (defaultMaxRetries > 0)
            Options.DefaultMaxRetries = defaultMaxRetries;
        if (defaultDelay is not null)
            Options.DefaultRetryDelay = defaultDelay.Value;

        return AddBehavior(typeof(RetryBehavior<,>));
    }

    public DispatchWeaveBuilder WithNotificationStrategy(NotificationPublishStrategy strategy)
    {
        Options.NotificationPublishStrategy = strategy;
        return this;
    }

    public DispatchWeaveBuilder WithNotificationPublisher(INotificationPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        Options.CustomNotificationPublisher = publisher;
        Registrar.RegisterInstance(publisher);
        return this;
    }

    public DispatchWeaveBuilder WithNotificationPublisher<TPublisher>(DispatchServiceLifetime? lifetime = null)
        where TPublisher : class, INotificationPublisher
    {
        Registrar.Register(typeof(INotificationPublisher), typeof(TPublisher), lifetime ?? DefaultLifetime, replace: true);
        return this;
    }

    /// <summary>
    /// Builds an <see cref="IDispatchRoutes"/> instance when backed by <see cref="StandaloneDispatchRegistrar"/>.
    /// </summary>
    public IDispatchRoutes BuildRoutes()
    {
        if (Registrar is StandaloneDispatchRegistrar standalone)
        {
            return new RegistryRoutes(standalone.Handlers, standalone.Pipelines, standalone.Notifications, Options);
        }

        if (Registrar is IServiceProvider sp)
        {
            return new ServiceProviderRoutes(sp, Options);
        }

        throw new InvalidOperationException(
            $"BuildRoutes() is intended for standalone builders or IServiceProvider registrars. Current registrar is '{Registrar.GetType().Name}'.");
    }

    /// <summary>
    /// Builds a standalone <see cref="IDispatcher"/> without requiring an external DI container.
    /// </summary>
    public IDispatcher Build()
    {
        var routes = BuildRoutes();
        var publisher = (Registrar as IServiceProvider)?.GetService(typeof(INotificationPublisher)) as INotificationPublisher;
        return new Dispatcher(routes, publisher, Options);
    }
}
