using System.Collections.Concurrent;

namespace DispatchWeave.Registration;

public class HandlerRegistry
{
    private readonly ConcurrentDictionary<(Type Request, Type Response), Func<object>> factories = new();
    private readonly ConcurrentDictionary<(Type Request, Type Response), Func<object>> streamFactories = new();

    public void Register<TRequest, TResponse>(Func<IRequestHandler<TRequest, TResponse>> factory)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(factory);
        Register(typeof(TRequest), typeof(TResponse), factory);
    }

    public void Register<TRequest, TResponse>(IRequestHandler<TRequest, TResponse> handler)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(handler);
        Register(typeof(TRequest), typeof(TResponse), () => handler);
    }

    public void Register<TRequest>(Func<IRequestHandler<TRequest>> factory)
        where TRequest : IRequest<Unit>
    {
        ArgumentNullException.ThrowIfNull(factory);
        Register(typeof(TRequest), typeof(Unit), factory);
    }

    public void Register<TRequest>(IRequestHandler<TRequest> handler)
        where TRequest : IRequest<Unit>
    {
        ArgumentNullException.ThrowIfNull(handler);
        Register(typeof(TRequest), typeof(Unit), () => handler);
    }

    public void Register(Type requestType, Type responseType, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(factory);
        factories[(requestType, responseType)] = factory;
    }

    public void RegisterStream<TRequest, TResponse>(Func<IStreamRequestHandler<TRequest, TResponse>> factory)
        where TRequest : IStreamRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(factory);
        RegisterStream(typeof(TRequest), typeof(TResponse), factory);
    }

    public void RegisterStream<TRequest, TResponse>(IStreamRequestHandler<TRequest, TResponse> handler)
        where TRequest : IStreamRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(handler);
        RegisterStream(typeof(TRequest), typeof(TResponse), () => handler);
    }

    public void RegisterStream(Type requestType, Type responseType, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(factory);
        streamFactories[(requestType, responseType)] = factory;
    }

    internal RequestHandlerDelegate<TResponse> Resolve<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IRequest<TResponse>
    {
        return () =>
        {
            if (!factories.TryGetValue((typeof(TRequest), typeof(TResponse)), out var factory))
                throw HandlerResolutionException.Missing(typeof(TRequest));

            var instance = factory() ?? throw HandlerResolutionException.NullFactory(typeof(TRequest));
            if (instance is not IRequestHandler<TRequest, TResponse> handler)
                throw HandlerResolutionException.InvalidHandlerType(typeof(TRequest), instance.GetType());

            return handler.Handle(request, token);
        };
    }

    internal StreamHandlerDelegate<TResponse> ResolveStream<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IStreamRequest<TResponse>
    {
        return () =>
        {
            if (!streamFactories.TryGetValue((typeof(TRequest), typeof(TResponse)), out var factory))
                throw HandlerResolutionException.Missing(typeof(TRequest));

            var instance = factory() ?? throw HandlerResolutionException.NullFactory(typeof(TRequest));
            if (instance is not IStreamRequestHandler<TRequest, TResponse> handler)
                throw HandlerResolutionException.InvalidHandlerType(typeof(TRequest), instance.GetType());

            return handler.Handle(request, token);
        };
    }
}
