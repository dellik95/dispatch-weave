using FluentResults;

namespace DispatchWeave.Behaviors;

/// <summary>
/// Pipeline behavior that caches responses for requests implementing <see cref="ICacheableRequest"/>.
/// Failed <see cref="IResultBase"/> responses are never cached.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(
    IDispatchCache cache,
    DispatchWeaveOptions? options = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        if (request is not ICacheableRequest cacheable || string.IsNullOrWhiteSpace(cacheable.CacheKey))
            return await next().ConfigureAwait(false);

        var (found, cachedValue) = await cache.TryGetAsync<TResponse>(cacheable.CacheKey, cancellationToken).ConfigureAwait(false);
        if (found && cachedValue is not null)
            return cachedValue;

        var response = await next().ConfigureAwait(false);

        if (response is not null && response is not IResultBase { IsFailed: true })
        {
            var ttl = cacheable.TimeToLive ?? options?.DefaultCacheTimeToLive;
            await cache.SetAsync(cacheable.CacheKey, response, ttl, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
