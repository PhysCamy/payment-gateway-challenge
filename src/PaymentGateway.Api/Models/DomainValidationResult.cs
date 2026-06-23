namespace PaymentGateway.Api.Models;

/// <summary>
/// Outcome of Layer 2 (domain) validation. Carries the human-readable reasons a request
/// was rejected so the API can surface them to the merchant; an empty <see cref="Errors"/>
/// collection means the request is valid.
/// </summary>
/// <param name="Errors">One actionable message per broken rule, in snake_case field vocabulary.</param>
public sealed record DomainValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
