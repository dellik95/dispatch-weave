namespace DispatchWeave.Behaviors;

/// <summary>
/// Pipeline behavior that runs all registered <see cref="IRequestPreProcessor{TRequest}"/> instances before invoking the next pipeline stage.
/// </summary>
public sealed class RequestPreProcessorBehavior<TRequest, TResponse>(
    IEnumerable<IRequestPreProcessor<TRequest>> preProcessors)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        foreach (var processor in preProcessors)
        {
            await processor.Process(request, cancellationToken).ConfigureAwait(false);
        }

        return await next().ConfigureAwait(false);
    }
}

/// <summary>
/// Pipeline behavior that runs all registered <see cref="IRequestPostProcessor{TRequest, TResponse}"/> instances after the request handler returns.
/// </summary>
public sealed class RequestPostProcessorBehavior<TRequest, TResponse>(
    IEnumerable<IRequestPostProcessor<TRequest, TResponse>> postProcessors)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var response = await next().ConfigureAwait(false);

        foreach (var processor in postProcessors)
        {
            await processor.Process(request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
