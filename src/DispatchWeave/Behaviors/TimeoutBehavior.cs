namespace DispatchWeave.Behaviors;

/// <summary>
/// Pipeline behavior that enforces an execution timeout for requests implementing <see cref="ITimeoutRequest"/>
/// or when <see cref="DispatchWeaveOptions.DefaultTimeout"/> is configured.
/// </summary>
public sealed class TimeoutBehavior<TRequest, TResponse>(DispatchWeaveOptions? options = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var timeout = (request as ITimeoutRequest)?.Timeout ?? options?.DefaultTimeout;
        if (timeout is null || timeout.Value <= TimeSpan.Zero)
            return await next().ConfigureAwait(false);

        try
        {
            return await next().WaitAsync(timeout.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"Request '{typeof(TRequest).Name}' timed out after {timeout.Value.TotalMilliseconds:F0} ms.",
                ex);
        }
    }
}
