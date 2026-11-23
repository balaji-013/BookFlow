using BookFlow.Application.Common.Models;
using FluentValidation;

namespace BookFlow.Application.Common.Extensions;

public static class ValidationExtensions
{
    public static async Task ValidateAndThrowAppAsync<T>(this IValidator<T> validator, T instance, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(instance, ct);
        if (result.IsValid) return;
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        throw new AppValidationException(errors);
    }

    public static IRuleBuilderOptions<T, string?> Gstin<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Matches("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$")
            .WithMessage("GSTIN format is invalid.");

    public static IRuleBuilderOptions<T, string?> IndianPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Matches(@"^\+?[0-9][0-9\- ]{7,14}$").WithMessage("Phone number is invalid.");
}
