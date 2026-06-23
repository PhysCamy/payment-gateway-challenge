using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Validators;

namespace PaymentGateway.Api.Tests.Unit;

/// <summary>
/// Layer 2 (domain) validation in isolation. <see cref="PostPaymentRequestValidator"/> is a
/// pure function over a request that has already cleared Layer 1, so these exercise the
/// domain rules directly (digit-only fields, the Luhn check, and the combined expiry not
/// being in the past) without booting the app. The exact messages are pinned so it stays
/// clear to the merchant what was wrong; the controller wiring that turns these into a
/// <c>400 { "status": "Rejected", ... }</c> response is covered by an integration test.
/// </summary>
public class PostPaymentRequestValidatorTests
{
    private readonly PostPaymentRequestValidator _validator = new();

    /// <summary>A request that passes the domain rules: valid Luhn card, 3-digit cvv, future expiry.</summary>
    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = 4,
        ExpiryYear = DateTime.UtcNow.Year + 1,
        Currency = Currency.GBP,
        Amount = 100,
        Cvv = "123"
    };

    public static IEnumerable<object[]> ValidRequests()
    {
        var thisMonth = DateTime.UtcNow;
        var nextMonth = thisMonth.AddMonths(1);

        yield return [ValidRequest()];
        yield return [With(r => r.CardNumber = new string('0', 16))]; // all-zeros is Luhn-valid
        yield return [With(r => r.Cvv = "1234")];                     // 4 digits is allowed
        yield return [With(r => { r.ExpiryMonth = thisMonth.Month; r.ExpiryYear = thisMonth.Year; })]; // this month — still valid
        yield return [With(r => { r.ExpiryMonth = nextMonth.Month; r.ExpiryYear = nextMonth.Year; })];
    }

    /// <summary>An invalid request paired with the exact error message it must surface.</summary>
    public static IEnumerable<object[]> InvalidRequests()
    {
        var lastMonth = DateTime.UtcNow.AddMonths(-1);

        // card_number — digits only, then Luhn (length 14–19 is already guaranteed by Layer 1)
        yield return [With(r => r.CardNumber = "222240534324887a"), "card_number must contain only digits."];
        yield return [With(r => r.CardNumber = "2222405343248878"), "card_number is not a valid card number."];

        // cvv — 3 or 4 digits
        yield return [With(r => r.Cvv = "12"), "cvv must be 3 or 4 characters long."];
        yield return [With(r => r.Cvv = "12345"), "cvv must be 3 or 4 characters long."];
        yield return [With(r => r.Cvv = "12a"), "cvv must contain only digits."];

        // expiry — combined month + year must not be in the past
        yield return
        [
            With(r => { r.ExpiryMonth = lastMonth.Month; r.ExpiryYear = lastMonth.Year; }),
            "expiry_month and expiry_year together must not be in the past."
        ];
    }

    [Theory]
    [MemberData(nameof(ValidRequests))]
    public void ValidRequest_HasNoErrors(PostPaymentRequest request)
    {
        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void InvalidRequest_ReportsReason(PostPaymentRequest request, string expectedMessage)
    {
        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(expectedMessage, result.Errors);
    }

    [Fact]
    public void EveryBrokenRule_IsReportedTogether()
    {
        var lastMonth = DateTime.UtcNow.AddMonths(-1);
        var request = With(r =>
        {
            r.CardNumber = "222240534324887a"; // non-digit
            r.Cvv = "12";                       // too short
            r.ExpiryMonth = lastMonth.Month;    // in the past
            r.ExpiryYear = lastMonth.Year;
        });

        var result = _validator.Validate(request);

        Assert.Equal(
            new[]
            {
                "card_number must contain only digits.",
                "cvv must be 3 or 4 characters long.",
                "expiry_month and expiry_year together must not be in the past."
            },
            result.Errors);
    }

    private static PostPaymentRequest With(Action<PostPaymentRequest> mutate)
    {
        var request = ValidRequest();
        mutate(request);
        return request;
    }
}
