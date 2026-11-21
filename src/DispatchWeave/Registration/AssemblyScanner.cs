using System.Reflection;
using DispatchWeave.Results;

namespace DispatchWeave.Registration;

/// <summary>
/// Categorizes a discovered handler or processor registration from assembly scanning.
/// </summary>
public enum DiscoveredContractKind
{
    RequestHandler,
    StreamRequestHandler,
    NotificationHandler,
    RequestPreProcessor,
    RequestPostProcessor,
    RequestExceptionHandler,
    RequestExceptionAction
}

/// <summary>
/// Represents a concrete implementation type and the closed service contract it fulfills.
/// </summary>
public readonly record struct DiscoveredRegistration(
    DiscoveredContractKind Kind,
    Type ServiceType,
    Type ImplementationType);

/// <summary>
/// Scans assemblies for concrete DispatchWeave request handlers, stream handlers, notification handlers,
/// pre/post-processors, and exception handlers/actions.
/// </summary>
public static class AssemblyScanner
{
    private static readonly HashSet<Type> DerivedRequestHandlerDefinitions =
    [
        typeof(IRequestHandler<>),
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>)
    ];

    private static readonly HashSet<Type> DerivedStreamHandlerDefinitions =
    [
        typeof(IStreamQueryHandler<,>)
    ];

    public static IReadOnlyList<DiscoveredRegistration> Scan(
        IEnumerable<Assembly> assemblies,
        Func<Type, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        List<DiscoveredRegistration> registrations = [];
        HashSet<(Type Service, Type Implementation)> seen = [];

        foreach (var assembly in assemblies.Where(static a => a is not null).Distinct())
        {
            foreach (var typeInfo in assembly.DefinedTypes)
            {
                if (!typeInfo.IsClass || typeInfo.IsAbstract || typeInfo.ContainsGenericParameters)
                    continue;

                var implementation = typeInfo.AsType();
                if (filter is not null && !filter(implementation))
                    continue;

                foreach (var iface in typeInfo.ImplementedInterfaces)
                {
                    if (!iface.IsGenericType)
                        continue;

                    var definition = iface.GetGenericTypeDefinition();
                    if (TryClassify(definition, out var kind))
                    {
                        if (seen.Add((iface, implementation)))
                        {
                            registrations.Add(new DiscoveredRegistration(kind, iface, implementation));
                        }
                    }
                }
            }
        }

        return registrations;
    }

    private static bool TryClassify(Type genericDefinition, out DiscoveredContractKind kind)
    {
        if (genericDefinition == typeof(IRequestHandler<,>) || DerivedRequestHandlerDefinitions.Contains(genericDefinition))
        {
            kind = DiscoveredContractKind.RequestHandler;
            return true;
        }

        if (genericDefinition == typeof(IStreamRequestHandler<,>) || DerivedStreamHandlerDefinitions.Contains(genericDefinition))
        {
            kind = DiscoveredContractKind.StreamRequestHandler;
            return true;
        }

        if (genericDefinition == typeof(INotificationHandler<>))
        {
            kind = DiscoveredContractKind.NotificationHandler;
            return true;
        }

        if (genericDefinition == typeof(IRequestPreProcessor<>))
        {
            kind = DiscoveredContractKind.RequestPreProcessor;
            return true;
        }

        if (genericDefinition == typeof(IRequestPostProcessor<,>))
        {
            kind = DiscoveredContractKind.RequestPostProcessor;
            return true;
        }

        if (genericDefinition == typeof(IRequestExceptionHandler<,,>))
        {
            kind = DiscoveredContractKind.RequestExceptionHandler;
            return true;
        }

        if (genericDefinition == typeof(IRequestExceptionAction<,>))
        {
            kind = DiscoveredContractKind.RequestExceptionAction;
            return true;
        }

        kind = default;
        return false;
    }
}
