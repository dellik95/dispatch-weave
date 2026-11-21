using FluentValidation;
using FluentValidation.Results;

namespace DispatchWeave.Results;

public static class RequestValidation
{
    public static async Task<List<ValidationFailure>> Validate<T>(
        T request,
        IEnumerable<IValidator<T>> validators,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validators);

        if (validators is ICollection<IValidator<T>> { Count: 0 })
            return [];

        List<ValidationFailure>? failures = null;
        ValidationContext<T>? context = null;

        foreach (var validator in validators)
        {
            context ??= new ValidationContext<T>(request);
            var result = await validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
            if (result.Errors.Count > 0)
            {
                failures ??= [];
                failures.AddRange(result.Errors);
            }
        }

        return failures ?? [];
    }
}
