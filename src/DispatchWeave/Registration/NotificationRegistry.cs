using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace DispatchWeave.Registration;

public class NotificationRegistry
{
    private readonly ConcurrentDictionary<Type, ImmutableArray<Func<object>>> handlers = new();

    public void AddHandler<TNotification>(INotificationHandler<TNotification> handler)
        where TNotification : INotification
    {
        AddHandler(typeof(TNotification), () => handler);
    }

    public void AddHandler<TNotification>(Func<INotificationHandler<TNotification>> factory)
        where TNotification : INotification
    {
        AddHandler(typeof(TNotification), () => factory());
    }

    public void AddHandler(Type notificationType, Func<object> factory)
    {
        handlers.AddOrUpdate(
            notificationType,
            _ => [factory],
            (_, existing) => existing.Add(factory));
    }

    public IEnumerable<INotificationHandler<TNotification>> GetHandlers<TNotification>()
        where TNotification : INotification
    {
        if (handlers.TryGetValue(typeof(TNotification), out var handlerArray))
        {
            return handlerArray.Select(factory => (INotificationHandler<TNotification>)factory());
        }

        return Enumerable.Empty<INotificationHandler<TNotification>>();
    }
}
