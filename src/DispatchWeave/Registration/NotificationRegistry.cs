using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace DispatchWeave.Registration;

public class NotificationRegistry
{
    private readonly ConcurrentDictionary<Type, ImmutableArray<Func<object>>> handlers = new();

    public void AddHandler<TNotification>(INotificationHandler<TNotification> handler)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(handler);
        AddHandler(typeof(TNotification), () => handler);
    }

    public void AddHandler<TNotification>(Func<INotificationHandler<TNotification>> factory)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(factory);
        AddHandler(typeof(TNotification), () => factory()!);
    }

    public void AddHandler(Type notificationType, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(notificationType);
        ArgumentNullException.ThrowIfNull(factory);

        if (!typeof(INotification).IsAssignableFrom(notificationType))
            throw HandlerResolutionException.InvalidNotification(notificationType);

        handlers.AddOrUpdate(
            notificationType,
            _ => [factory],
            (_, existing) => existing.Add(factory));
    }

    public IEnumerable<INotificationHandler<TNotification>> GetHandlers<TNotification>()
        where TNotification : INotification
    {
        if (!handlers.TryGetValue(typeof(TNotification), out var handlerArray))
            return [];

        return ResolveTyped<TNotification>(handlerArray);
    }

    public IEnumerable<INotificationHandler<TNotification>> GetPolymorphicHandlers<TNotification>()
        where TNotification : INotification
    {
        var targetType = typeof(TNotification);
        List<INotificationHandler<TNotification>> resolved = [];

        foreach (var (registeredType, factories) in handlers)
        {
            if (registeredType.IsAssignableFrom(targetType))
            {
                foreach (var factory in factories)
                {
                    var instance = factory() ?? throw HandlerResolutionException.NullFactory(registeredType);
                    if (instance is INotificationHandler<TNotification> typed)
                    {
                        resolved.Add(typed);
                    }
                }
            }
        }

        return resolved;
    }

    private static IEnumerable<INotificationHandler<TNotification>> ResolveTyped<TNotification>(
        ImmutableArray<Func<object>> factories)
        where TNotification : INotification
    {
        foreach (var factory in factories)
        {
            var instance = factory() ?? throw HandlerResolutionException.NullFactory(typeof(TNotification));
            if (instance is not INotificationHandler<TNotification> typed)
                throw HandlerResolutionException.InvalidHandlerType(typeof(TNotification), instance.GetType());
            yield return typed;
        }
    }
}
