namespace DispatchWeave.Registration;

public class PipelineRegistry
{
    private sealed record PipelineRegistration(Type InterfaceType, Func<object> Factory);
    private sealed record OpenPipelineRegistration(Type BehaviorType, Func<Type, object?> Factory);

    private PipelineRegistration[] behaviors = [];
    private OpenPipelineRegistration[] openBehaviors = [];

    public void AddBehavior<TRequest, TResult>(IPipelineBehavior<TRequest, TResult> behavior)
        where TRequest : IRequest<TResult>
    {
        var interfaceType = typeof(IPipelineBehavior<TRequest, TResult>);
        AppendLockFree(ref behaviors, new PipelineRegistration(interfaceType, () => behavior));
    }

    public void AddBehavior<TRequest, TResult>(Func<IPipelineBehavior<TRequest, TResult>> factory)
        where TRequest : IRequest<TResult>
    {
        var interfaceType = typeof(IPipelineBehavior<TRequest, TResult>);
        AppendLockFree(ref behaviors, new PipelineRegistration(interfaceType, () => factory()));
    }

    public void AddBehavior(Type requestType, Type resultType, Func<object> factory)
    {
        var interfaceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, resultType);
        AppendLockFree(ref behaviors, new PipelineRegistration(interfaceType, factory));
    }

    public void AddOpenBehavior(Type behaviorType, Func<Type, object?> factory)
    {
        if (!behaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException("Behavior type must be an open generic definition.", nameof(behaviorType));
        }

        AppendLockFree(ref openBehaviors, new OpenPipelineRegistration(behaviorType, factory));
    }

    private static void AppendLockFree<T>(ref T[] array, T item)
    {
        while (true)
        {
            var current = Volatile.Read(ref array);
            T[] next = [.. current, item];
            if (ReferenceEquals(Interlocked.CompareExchange(ref array, next, current), current))
            {
                return;
            }
        }
    }

    public IEnumerable<IPipelineBehavior<TRequest, TResult>> GetBehaviors<TRequest, TResult>(bool includeOpen = true)
        where TRequest : IRequest<TResult>
    {
        var interfaceType = typeof(IPipelineBehavior<TRequest, TResult>);
        var currentBehaviors = Volatile.Read(ref behaviors);
        var resolved = currentBehaviors
            .Where(b => b.InterfaceType == interfaceType)
            .Select(b => (IPipelineBehavior<TRequest, TResult>)b.Factory());

        if (!includeOpen) return resolved;

        var currentOpen = Volatile.Read(ref openBehaviors);
        var openResolved = currentOpen
            .Select(InstantiateOpenBehavior<TRequest, TResult>)
            .Where(static behavior => behavior is not null)
            .Cast<IPipelineBehavior<TRequest, TResult>>();

        return resolved.Concat(openResolved);
    }

    // A behavior registered for a base request contract can wrap any compatible concrete request.
    public IEnumerable<IPipelineBehavior<TRequest, TResult>> GetInheritedBehaviors<TRequest, TResult>()
        where TRequest : IRequest<TResult>
    {
        var requested = typeof(IPipelineBehavior<TRequest, TResult>);
        return Volatile.Read(ref behaviors)
            .Where(entry => entry.InterfaceType != requested && requested.IsAssignableFrom(entry.InterfaceType))
            .Select(entry => (IPipelineBehavior<TRequest, TResult>)entry.Factory());
    }

    private static IPipelineBehavior<TRequest, TResult>? InstantiateOpenBehavior<TRequest, TResult>(OpenPipelineRegistration registration)
        where TRequest : IRequest<TResult>
    {
        Type closedType;
        try
        {
            closedType = registration.BehaviorType.MakeGenericType(typeof(TRequest), typeof(TResult));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (TypeLoadException)
        {
            return null;
        }

        var instance = registration.Factory(closedType);
        if (instance is null)
        {
            throw new InvalidOperationException($"Factory returned null for open pipeline behavior {registration.BehaviorType.FullName}.");
        }

        if (instance is not IPipelineBehavior<TRequest, TResult> typedBehavior)
        {
            throw new InvalidOperationException($"Resolved instance {instance.GetType().FullName} does not implement IPipelineBehavior<{typeof(TRequest).FullName}, {typeof(TResult).FullName}>.");
        }

        return typedBehavior;
    }
}
