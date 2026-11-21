namespace DispatchWeave;

/// <summary>
/// Container-agnostic service lifetime for handlers, behaviors, and dispatcher services.
/// </summary>
public enum DispatchServiceLifetime
{
    Scoped = 0,
    Transient = 1,
    Singleton = 2
}

/// <summary>
/// Abstraction over a dependency registration mechanism (DI container or standalone registry).
/// Implement this interface to plug any DI container into <c>DispatchWeaveBuilder</c>.
/// </summary>
public interface IDispatchRegistrar
{
    /// <summary>
    /// Registers a pre-created singleton instance for <typeparamref name="TService"/>.
    /// </summary>
    void RegisterInstance<TService>(TService instance) where TService : class;

    /// <summary>
    /// Registers a single service mapping if not already registered, or replaces it when <paramref name="replace"/> is <see langword="true"/>.
    /// </summary>
    void Register(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped,
        bool replace = false);

    /// <summary>
    /// Appends a concrete or open-generic implementation to a multi-instance service collection
    /// (such as pipeline behaviors, notification handlers, processors, or validators).
    /// </summary>
    void RegisterCollection(
        Type serviceType,
        Type implementationType,
        DispatchServiceLifetime lifetime = DispatchServiceLifetime.Scoped);

    /// <summary>
    /// Appends a pre-created instance to a multi-instance service collection.
    /// </summary>
    void RegisterCollectionInstance(Type serviceType, object instance);
}
