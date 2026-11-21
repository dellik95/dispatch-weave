using DispatchWeave.Results;
using FluentValidation;

namespace DispatchWeave.Behaviors;

/// <summary>
/// Pipeline behavior that validates requests using <see cref="IValidator{T}"/> and throws <see cref="ValidationException"/> when any failures occur.
/// Suitable for requests whose response type does not inherit from <c>FluentResults.ResultBase</c>.
/// </summary>
public sealed class ThrowingValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var failures = await RequestValidation.Validate(request, validators, cancellationToken).ConfigureAwait(false);
        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next().ConfigureAwait(false);
    }
}
