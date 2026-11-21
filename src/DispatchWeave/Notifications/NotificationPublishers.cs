namespace DispatchWeave.Notifications;

/// <summary>
/// Publishes notifications sequentially in registration order, stopping on the first exception.
/// </summary>
public sealed class SequentialNotificationPublisher : INotificationPublisher
{
    public static readonly SequentialNotificationPublisher Instance = new();

    public async Task Publish<T>(IEnumerable<INotificationHandler<T>> handlers, T notification, CancellationToken token)
        where T : INotification
    {
        ArgumentNullException.ThrowIfNull(handlers);
        foreach (var handler in handlers)
        {
            token.ThrowIfCancellationRequested();
            await handler.Handle(notification, token).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Publishes notifications sequentially to all handlers even if one or more fail,
/// throwing an <see cref="AggregateException"/> containing all failures after all handlers have run.
/// </summary>
public sealed class ContinueOnExceptionNotificationPublisher : INotificationPublisher
{
    public static readonly ContinueOnExceptionNotificationPublisher Instance = new();

    public async Task Publish<T>(IEnumerable<INotificationHandler<T>> handlers, T notification, CancellationToken token)
        where T : INotification
    {
        ArgumentNullException.ThrowIfNull(handlers);
        List<Exception>? exceptions = null;

        foreach (var handler in handlers)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await handler.Handle(notification, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
            throw new AggregateException("One or more notification handlers threw an exception.", exceptions);
    }
}

/// <summary>
/// Publishes notifications concurrently to all handlers using <see cref="Task.WhenAll(IEnumerable{Task})"/>.
/// If multiple handlers fail, throws an <see cref="AggregateException"/> with all exceptions.
/// </summary>
public sealed class ParallelNotificationPublisher : INotificationPublisher
{
    public static readonly ParallelNotificationPublisher Instance = new();

    public async Task Publish<T>(IEnumerable<INotificationHandler<T>> handlers, T notification, CancellationToken token)
        where T : INotification
    {
        ArgumentNullException.ThrowIfNull(handlers);
        token.ThrowIfCancellationRequested();

        var handlerList = handlers as IReadOnlyList<INotificationHandler<T>> ?? handlers.ToArray();
        if (handlerList.Count == 0)
            return;

        if (handlerList.Count == 1)
        {
            await handlerList[0].Handle(notification, token).ConfigureAwait(false);
            return;
        }

        var tasks = new Task[handlerList.Count];
        for (var i = 0; i < handlerList.Count; i++)
        {
            var handler = handlerList[i];
            tasks[i] = InvokeHandlerAsync(handler, notification, token);
        }

        var whenAll = Task.WhenAll(tasks);
        try
        {
            await whenAll.ConfigureAwait(false);
        }
        catch
        {
            if (whenAll.Exception is { InnerExceptions.Count: > 1 } aggregate)
                throw aggregate;
            throw;
        }
    }

    private static async Task InvokeHandlerAsync<T>(INotificationHandler<T> handler, T notification, CancellationToken token)
        where T : INotification
    {
        await handler.Handle(notification, token).ConfigureAwait(false);
    }
}

/// <summary>
/// Schedules notification handlers to run asynchronously in the background without blocking the caller.
/// </summary>
public sealed class FireAndForgetNotificationPublisher(Action<Exception, INotification>? onError = null) : INotificationPublisher
{
    public Task Publish<T>(IEnumerable<INotificationHandler<T>> handlers, T notification, CancellationToken token)
        where T : INotification
    {
        ArgumentNullException.ThrowIfNull(handlers);
        token.ThrowIfCancellationRequested();

        var handlerArray = handlers as INotificationHandler<T>[] ?? handlers.ToArray();
        if (handlerArray.Length == 0)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            foreach (var handler in handlerArray)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    await handler.Handle(notification, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex, notification);
                }
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}
