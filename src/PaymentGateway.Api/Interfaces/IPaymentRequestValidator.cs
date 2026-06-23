using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Interfaces;

/// <summary>
/// Layer 2 (domain) validation, applied in the controller after model binding succeeds.
/// Enforces business and format rules that data annotations cannot express (digit-only
/// fields, the Luhn check, and the combined expiry month/year not being in the past).
/// </summary>
public interface IPaymentRequestValidator
{
    /// <summary>
    /// Validates the bound request against the domain rules.
    /// </summary>
    /// <param name="request">A request that has already cleared Layer 1 schema validation.</param>
    /// <returns>A result describing whether the request is valid and, if not, why.</returns>
    DomainValidationResult Validate(PostPaymentRequest request);
}
