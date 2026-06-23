using LuhnDotNet.Algorithm.Luhn;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Validators;

/// <summary>
/// Sole implementation of <see cref="IPaymentRequestValidator"/>. Stateless and safe to
/// register as a singleton. Assumes Layer 1 has already guaranteed the fields are present
/// and within their schema ranges (card 14–19 chars, cvv 1–50 chars, month 1–12).
/// </summary>
public sealed class PostPaymentRequestValidator : IPaymentRequestValidator
{
    public DomainValidationResult Validate(PostPaymentRequest request)
    {
        var errors = new List<string>();

        ValidateCardNumber(request.CardNumber, errors);
        ValidateCvv(request.Cvv, errors);
        ValidateExpiry(request.ExpiryMonth, request.ExpiryYear, errors);

        return new DomainValidationResult(errors);
    }

    private static void ValidateCardNumber(string? cardNumber, List<string> errors)
    {
        if (string.IsNullOrEmpty(cardNumber) || !IsAllDigits(cardNumber))
        {
            errors.Add("card_number must contain only digits.");
            return;
        }

        // IsValidLuhnNumber throws on non-digit input, so only reachable once digits are confirmed.
        if (!LuhnValidator.IsValidLuhnNumber(cardNumber))
        {
            errors.Add("card_number is not a valid card number.");
        }
    }

    private static void ValidateCvv(string? cvv, List<string> errors)
    {
        if (cvv is null or { Length: < 3 or > 4 })
        {
            errors.Add("cvv must be 3 or 4 characters long.");
        }

        if (!string.IsNullOrEmpty(cvv) && !IsAllDigits(cvv))
        {
            errors.Add("cvv must contain only digits.");
        }
    }

    private static void ValidateExpiry(int? expiryMonth, int? expiryYear, List<string> errors)
    {
        if (expiryMonth is null || expiryYear is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var isInThePast = expiryYear < now.Year ||
            (expiryYear == now.Year && expiryMonth < now.Month);

        if (isInThePast)
        {
            errors.Add("expiry_month and expiry_year together must not be in the past.");
        }
    }

    private static bool IsAllDigits(string value) => value.All(c => c is >= '0' and <= '9');
}
