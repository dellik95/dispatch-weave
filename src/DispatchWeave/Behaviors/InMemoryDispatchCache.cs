using System.Collections.Concurrent;

namespace DispatchWeave.Behaviors;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IDispatchCache"/> supporting time-to-live expiration via <see cref="TimeProvider"/>.
/// </summary>
public sealed class InMemoryDispatchCache(TimeProvider? timeProvider = null) : IDispatchCache
{
    private sealed record CacheEntry(object? Value, DateTimeOffset? ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntry> store = new(StringComparer.Ordinal);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (!store.TryGetValue(key, out var entry))
            return ValueTask.FromResult<(bool, T?)>((false, default));

        if (entry.ExpiresAt is { } expiresAt && clock.GetUtcNow() >= expiresAt)
        {
            store.TryRemove(key, out _);
            return ValueTask.FromResult<(bool, T?)>((false, default));
        }

        if (entry.Value is T typed)
            return ValueTask.FromResult<(bool, T?)>((true, typed));

        return ValueTask.FromResult<(bool, T?)>((false, default));
    }

    public ValueTask SetAsync<T>(string key, T value, TimeSpan? timeToLive, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (timeToLive is { } ttl && ttl <= TimeSpan.Zero)
        {
            store.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }

        DateTimeOffset? expiresAt = timeToLive is { } duration
            ? clock.GetUtcNow().Add(duration)
            : null;

        store[key] = new CacheEntry(value, expiresAt);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        store.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public void Clear() => store.Clear();
}
