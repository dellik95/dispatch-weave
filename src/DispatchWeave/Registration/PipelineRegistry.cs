using System.Collections.Concurrent;

namespace DispatchWeave.Registration;

public class PipelineRegistry
{
    private sealed record PipelineRegistration(Type InterfaceType, Func<object> Factory);
    private sealed record OpenPipelineRegistration(Type BehaviorType, Func<Type, object?> Factory);

    private static readonly ConcurrentDictionary<(Type OpenType, Type RequestType, Type ResultType), Type?> ClosedTypeCache = new();

    private PipelineRegistration[] behaviors = [];
    private OpenPipelineRegistration[] openBehaviors = [];
    private PipelineRegistration[] streamBehaviors = [];
    private OpenPipelineRegistration[] openStreamBehaviors = [];

    public void AddBehavior<TRequest, TResult>(IPipelineBehavior<TRequest, TResult> behavior)
        where TRequest : IRequest<TResult>
    {
        ArgumentNullException.ThrowIfNull(behavior);
        var interfaceType = typeof(IPipelineBehavior<TRequest, TResult>);
        AppendLockFree(ref behaviors, new PipelineRegistration(interfaceType, () => behavior));
    }

    public void AddBehavior<TRequest, TResult>(Func<IPipelineBehavior<TRequest, TResult>> factory)
        where TRequest : IRequest<TResult>
    {
        ArgumentNullException.ThrowIfNull(factory);
        var interfaceType = typeof(IPipelineBehavior<TRequest, TResult>);
        AppendLockFree(ref behaviors, new PipelineRegistration(interfaceType, () => factory()));
    }

    public void AddBehavior(Type requestType, Type resultType, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(factory);
        var interfaceType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, resultType);
        AppendLockFree(ref behaviors, new PipelineRegistration(interfaceType, factory));
    }

    public void AddOpenBehavior(Type behaviorType, Func<Type, object?> factory)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);
        ArgumentNullException.ThrowIfNull(factory);
        if (!behaviorType.IsGenericTypeDefinition)
            throw new ArgumentException("Behavior type must be an open generic definition.", nameof(behaviorType));

        AppendLockFree(ref openBehaviors, new OpenPipelineRegistration(behaviorType, factory));
    }

    public void AddStreamBehavior<TRequest, TResult>(IStreamPipelineBehavior<TRequest, TResult> behavior)
        where TRequest : IStreamRequest<TResult>
    {
        ArgumentNullException.ThrowIfNull(behavior);
        var interfaceType = typeof(IStreamPipelineBehavior<TRequest, TResult>);
        AppendLockFree(ref streamBehaviors, new PipelineRegistration(interfaceType, () => behavior));
    }

    public void AddStreamBehavior<TRequest, TResult>(Func<IStreamPipelineBehavior<TRequest, TResult>> factory)
        where TRequest : IStreamRequest<TResult>
    {
        ArgumentNullException.ThrowIfNull(factory);
        var interfaceType = typeof(IStreamPipelineBehavior<TRequest, TResult>);
        AppendLockFree(ref streamBehaviors, new PipelineRegistration(interfaceType, () => factory()));
    }

    public void AddStreamBehavior(Type requestType, Type resultType, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(factory);
        var interfaceType = typeof(IStreamPipelineBehavior<,>).MakeGenericType(requestType, resultType);
        AppendLockFree(ref streamBehaviors, new PipelineRegistration(interfaceType, factory));
    }

    public void AddOpenStreamBehavior(Type behaviorType, Func<Type, object?> factory)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);
        ArgumentNullException.ThrowIfNull(factory);
        if (!behaviorType.IsGenericTypeDefinition)
            throw new ArgumentException("Stream behavior type must be an open generic definition.", nameof(behaviorType));

        AppendLockFree(ref openStreamBehaviors, new OpenPipelineRegistration(behaviorType, factory));
    }

    private static void AppendLockFree<T>(ref T[] array, T item)
    {
        while (true)
        {
            var current = Volatile.Read(ref array);
            T[] next = [.. current, item];
            if (ReferenceEquals(Interlocked.CompareExchange(ref array, next, current), current))
                return;
        }
    }

    public IEnumerable<IPipelineBehavior<TRequest, TResult>> GetBehaviors<TRequest, TResult>(
        bool includeOpen = true,
        bool includeInherited = false)
        where TRequest : IRequest<TResult>
    {
        var interfaceType = typeof(IPipelineBehavior<TRequest, TResult>);
        var currentBehaviors = Volatile.Read(ref behaviors);

        var resolved = currentBehaviors
            .Where(b => includeInherited
                ? interfaceType.IsAssignableFrom(b.InterfaceType)
                : b.InterfaceType == interfaceType)
            .Select(b => (IPipelineBehavior<TRequest, TResult>)b.Factory());

        if (!includeOpen)
            return resolved;

        var currentOpen = Volatile.Read(ref openBehaviors);
        if (currentOpen.Length == 0)
            return resolved;

        var openResolved = currentOpen
            .Select(InstantiateOpenBehavior<TRequest, TResult, IPipelineBehavior<TRequest, TResult>>)
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

    public IEnumerable<IStreamPipelineBehavior<TRequest, TResult>> GetStreamBehaviors<TRequest, TResult>(
        bool includeOpen = true,
        bool includeInherited = false)
        where TRequest : IStreamRequest<TResult>
    {
        var interfaceType = typeof(IStreamPipelineBehavior<TRequest, TResult>);
        var currentBehaviors = Volatile.Read(ref streamBehaviors);

        var resolved = currentBehaviors
            .Where(b => includeInherited
                ? interfaceType.IsAssignableFrom(b.InterfaceType)
                : b.InterfaceType == interfaceType)
            .Select(b => (IStreamPipelineBehavior<TRequest, TResult>)b.Factory());

        if (!includeOpen)
            return resolved;

        var currentOpen = Volatile.Read(ref openStreamBehaviors);
        if (currentOpen.Length == 0)
            return resolved;

        var openResolved = currentOpen
            .Select(InstantiateOpenBehavior<TRequest, TResult, IStreamPipelineBehavior<TRequest, TResult>>)
            .Where(static behavior => behavior is not null)
            .Cast<IStreamPipelineBehavior<TRequest, TResult>>();

        return resolved.Concat(openResolved);
    }

    public IEnumerable<IStreamPipelineBehavior<TRequest, TResult>> GetInheritedStreamBehaviors<TRequest, TResult>()
        where TRequest : IStreamRequest<TResult>
    {
        var requested = typeof(IStreamPipelineBehavior<TRequest, TResult>);
        return Volatile.Read(ref streamBehaviors)
            .Where(entry => entry.InterfaceType != requested && requested.IsAssignableFrom(entry.InterfaceType))
            .Select(entry => (IStreamPipelineBehavior<TRequest, TResult>)entry.Factory());
    }

    private static TBehaviorContract? InstantiateOpenBehavior<TRequest, TResult, TBehaviorContract>(
        OpenPipelineRegistration registration)
        where TBehaviorContract : class
    {
        var closedType = ClosedTypeCache.GetOrAdd(
            (registration.BehaviorType, typeof(TRequest), typeof(TResult)),
            static key =>
            {
                try
                {
                    return key.OpenType.MakeGenericType(key.RequestType, key.ResultType);
                }
                catch (ArgumentException)
                {
                    return null;
                }
                catch (TypeLoadException)
                {
                    return null;
                }
            });

        if (closedType is null)
            return null;

        var instance = registration.Factory(closedType)
            ?? throw new InvalidOperationException($"Factory returned null for open pipeline behavior {registration.BehaviorType.FullName}.");

        if (instance is not TBehaviorContract typedBehavior)
        {
            throw new InvalidOperationException(
                $"Resolved instance {instance.GetType().FullName} does not implement {typeof(TBehaviorContract).FullName}.");
        }

        return typedBehavior;
    }
}
