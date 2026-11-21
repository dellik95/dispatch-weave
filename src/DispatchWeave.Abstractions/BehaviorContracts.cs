namespace DispatchWeave;

/// <summary>
/// Marker interface for requests whose responses should be cached by the caching pipeline behavior.
/// </summary>
public interface ICacheableRequest
{
    /// <summary>
    /// Unique cache key for this request instance.
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    /// Optional time-to-live duration for the cached response.
    /// </summary>
    TimeSpan? TimeToLive => null;
}

/// <summary>
/// Abstraction over a cache store used by the caching pipeline behavior.
/// </summary>
public interface IDispatchCache
{
    ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default);
    ValueTask SetAsync<T>(string key, T value, TimeSpan? timeToLive, CancellationToken cancellationToken = default);
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker interface for requests that enforce a maximum execution timeout.
/// </summary>
public interface ITimeoutRequest
{
    TimeSpan Timeout { get; }
}

/// <summary>
/// Marker interface for requests that should be automatically retried on transient failures.
/// </summary>
public interface IRetryableRequest
{
    int MaxRetries { get; }

    TimeSpan Delay => TimeSpan.Zero;

    bool ShouldRetry(Exception exception) => exception is not OperationCanceledException;
}
