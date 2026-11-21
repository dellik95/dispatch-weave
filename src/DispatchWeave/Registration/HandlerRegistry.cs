using System.Collections.Concurrent;

namespace DispatchWeave.Registration;

public class HandlerRegistry
{
    private readonly ConcurrentDictionary<(Type Request, Type Response), Func<object>> factories = new();

    public void Register<TRequest, TResponse>(Func<IRequestHandler<TRequest, TResponse>> factory)
        where TRequest : IRequest<TResponse> => Register(typeof(TRequest), typeof(TResponse), factory);

    public void Register(Type requestType, Type responseType, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        factories[(requestType, responseType)] = factory;
    }

    internal RequestHandlerDelegate<TResponse> Resolve<TRequest, TResponse>(TRequest request, CancellationToken token)
        where TRequest : IRequest<TResponse>
    {
        if (!factories.TryGetValue((typeof(TRequest), typeof(TResponse)), out var factory))
            throw HandlerResolutionException.Missing(typeof(TRequest));
        var handler = (IRequestHandler<TRequest, TResponse>?)factory() ??
            throw HandlerResolutionException.NullFactory(typeof(TRequest));
        return () => handler.Handle(request, token);
    }
}
