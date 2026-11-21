using FluentValidation;
using FluentValidation.Results;

namespace DispatchWeave.Results;

public static class RequestValidation
{
    public static async Task<List<ValidationFailure>> Validate<T>(T request, IEnumerable<IValidator<T>> validators,
        CancellationToken cancellationToken)
    {
        List<ValidationFailure> failures = [];
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(new ValidationContext<T>(request), cancellationToken).ConfigureAwait(false);
            failures.AddRange(result.Errors);
        }
        return failures;
    }
}
