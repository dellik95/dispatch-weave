namespace DispatchWeave.Behaviors;

/// <summary>
/// Pipeline behavior that automatically retries failed request executions for requests implementing <see cref="IRetryableRequest"/>
/// or when <see cref="DispatchWeaveOptions.DefaultMaxRetries"/> is configured.
/// </summary>
public sealed class RetryBehavior<TRequest, TResponse>(
    DispatchWeaveOptions? options = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var retryable = request as IRetryableRequest;
        var maxRetries = retryable?.MaxRetries ?? options?.DefaultMaxRetries ?? 0;

        if (maxRetries <= 0)
            return await next().ConfigureAwait(false);

        var delay = retryable?.Delay ?? options?.DefaultRetryDelay ?? TimeSpan.Zero;
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await next().ConfigureAwait(false);
            }
            catch (Exception ex) when (
                attempt < maxRetries &&
                !cancellationToken.IsCancellationRequested &&
                (retryable?.ShouldRetry(ex) ?? ex is not OperationCanceledException))
            {
                attempt++;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, TimeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
