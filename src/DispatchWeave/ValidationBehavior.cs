using FluentResults;
using FluentValidation;

namespace DispatchWeave.Results;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : ResultBase<TResponse>, new()
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var failures = await RequestValidation.Validate(request, validators, cancellationToken).ConfigureAwait(false);
        if (failures.Count == 0)
            return await next().ConfigureAwait(false);

        var response = new TResponse();
        foreach (var failure in failures)
            response.WithError(new Error(failure.ErrorMessage)
                .WithMetadata("PropertyName", failure.PropertyName)
                .WithMetadata("ErrorCode", failure.ErrorCode));
        return response;
    }
}
