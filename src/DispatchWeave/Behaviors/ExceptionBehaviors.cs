using System.Collections.Concurrent;
using System.Reflection;

namespace DispatchWeave.Behaviors;

/// <summary>
/// Pipeline behavior that executes registered <see cref="IRequestExceptionAction{TRequest, TException}"/> callbacks
/// when an exception occurs, then rethrows the exception.
/// </summary>
public sealed class RequestExceptionActionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, ExceptionActionInvoker> Invokers = new();
    private readonly Func<Type, IEnumerable<object>> resolveAll;

    public RequestExceptionActionBehavior(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        resolveAll = serviceType =>
        {
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
            return (serviceProvider.GetService(enumerableType) as IEnumerable<object>) ?? [];
        };
    }

    internal RequestExceptionActionBehavior(Func<Type, IEnumerable<object>> resolveAll)
    {
        ArgumentNullException.ThrowIfNull(resolveAll);
        this.resolveAll = resolveAll;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            for (var exType = exception.GetType();
                 exType is not null && typeof(Exception).IsAssignableFrom(exType);
                 exType = exType.BaseType)
            {
                var invoker = Invokers.GetOrAdd(exType, static t =>
                    (ExceptionActionInvoker)Activator.CreateInstance(
                        typeof(ExceptionActionInvoker<>).MakeGenericType(typeof(TRequest), typeof(TResponse), t))!);

                await invoker.Invoke(resolveAll, request, exception, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }

    private abstract class ExceptionActionInvoker
    {
        public abstract Task Invoke(
            Func<Type, IEnumerable<object>> resolveAll,
            TRequest request,
            Exception exception,
            CancellationToken token);
    }

    private sealed class ExceptionActionInvoker<TEx> : ExceptionActionInvoker
        where TEx : Exception
    {
        public override async Task Invoke(
            Func<Type, IEnumerable<object>> resolveAll,
            TRequest request,
            Exception exception,
            CancellationToken token)
        {
            var typedException = (TEx)exception;
            foreach (var action in resolveAll(typeof(IRequestExceptionAction<TRequest, TEx>)).Cast<IRequestExceptionAction<TRequest, TEx>>())
            {
                await action.Execute(request, typedException, token).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Pipeline behavior that invokes matching <see cref="IRequestExceptionHandler{TRequest, TResponse, TException}"/> instances
/// from most-derived exception type to <see cref="Exception"/>, returning the fallback response if marked handled.
/// </summary>
public sealed class RequestExceptionHandlerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, ExceptionHandlerInvoker> Invokers = new();
    private readonly Func<Type, IEnumerable<object>> resolveAll;

    public RequestExceptionHandlerBehavior(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        resolveAll = serviceType =>
        {
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
            return (serviceProvider.GetService(enumerableType) as IEnumerable<object>) ?? [];
        };
    }

    internal RequestExceptionHandlerBehavior(Func<Type, IEnumerable<object>> resolveAll)
    {
        ArgumentNullException.ThrowIfNull(resolveAll);
        this.resolveAll = resolveAll;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var state = new RequestExceptionHandlerState<TResponse>();

            for (var exType = exception.GetType();
                 exType is not null && typeof(Exception).IsAssignableFrom(exType);
                 exType = exType.BaseType)
            {
                var invoker = Invokers.GetOrAdd(exType, static t =>
                    (ExceptionHandlerInvoker)Activator.CreateInstance(
                        typeof(ExceptionHandlerInvoker<>).MakeGenericType(typeof(TRequest), typeof(TResponse), t))!);

                await invoker.Invoke(resolveAll, request, exception, state, cancellationToken).ConfigureAwait(false);
                if (state.Handled)
                    return state.Response!;
            }

            throw;
        }
    }

    private abstract class ExceptionHandlerInvoker
    {
        public abstract Task Invoke(
            Func<Type, IEnumerable<object>> resolveAll,
            TRequest request,
            Exception exception,
            RequestExceptionHandlerState<TResponse> state,
            CancellationToken token);
    }

    private sealed class ExceptionHandlerInvoker<TEx> : ExceptionHandlerInvoker
        where TEx : Exception
    {
        public override async Task Invoke(
            Func<Type, IEnumerable<object>> resolveAll,
            TRequest request,
            Exception exception,
            RequestExceptionHandlerState<TResponse> state,
            CancellationToken token)
        {
            var typedException = (TEx)exception;

            foreach (var handler in resolveAll(typeof(IRequestExceptionHandler<TRequest, TResponse, TEx>))
                         .Cast<IRequestExceptionHandler<TRequest, TResponse, TEx>>())
            {
                await handler.Handle(request, typedException, state, token).ConfigureAwait(false);
                if (state.Handled)
                    return;
            }
        }
    }
}
