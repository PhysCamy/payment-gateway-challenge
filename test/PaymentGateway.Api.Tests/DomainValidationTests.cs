using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;

using PaymentGateway.Api.Controllers;

namespace PaymentGateway.Api.Tests;

/// <summary>
/// Layer 2 (domain) validation runs inside the controller action after model binding
/// succeeds, so these are integration tests driven through <see cref="WebApplicationFactory{T}"/>.
/// Every payload here clears Layer 1, so a rejection can only come from the domain rules
/// (digit-only fields, the Luhn check, and the combined expiry not being in the past). A
/// failure returns <c>400</c> with <c>{ "status": "Rejected", "errors": [...] }</c>; the
/// assertions pin the exact messages so it stays clear to the merchant what was wrong.
/// </summary>
public class DomainValidationTests
{
    private readonly HttpClient _client =
        new PaymentGatewayApplicationFactory().CreateClient();

    private sealed record Rejection(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("errors")] string[] Errors);

    /// <summary>A payload that passes both validation layers: valid Luhn card, 3-digit cvv, future expiry.</summary>
    private static Dictionary<string, object?> ValidPayload() => new()
    {
        ["card_number"] = "2222405343248877",
        ["expiry_month"] = 4,
        ["expiry_year"] = DateTime.UtcNow.Year + 1,
        ["currency"] = "GBP",
        ["amount"] = 100,
        ["cvv"] = "123"
    };

    private static Dictionary<string, object?> With(string field, object? value)
    {
        var payload = ValidPayload();
        payload[field] = value;
        return payload;
    }

    private static Dictionary<string, object?> WithExpiry(int month, int year)
    {
        var payload = ValidPayload();
        payload["expiry_month"] = month;
        payload["expiry_year"] = year;
        return payload;
    }

    public static IEnumerable<object[]> ValidDomainPayloads()
    {
        var thisMonth = DateTime.UtcNow;
        var nextMonth = thisMonth.AddMonths(1);

        yield return [ValidPayload()];
        yield return [With("card_number", new string('0', 16))]; // all-zeros is Luhn-valid
        yield return [With("cvv", "1234")];                       // 4 digits is allowed
        yield return [WithExpiry(thisMonth.Month, thisMonth.Year)]; // expires this month — still valid
        yield return [WithExpiry(nextMonth.Month, nextMonth.Year)];
    }

    /// <summary>An invalid payload paired with the exact error message it must surface.</summary>
    public static IEnumerable<object[]> RejectedDomainPayloads()
    {
        var lastMonth = DateTime.UtcNow.AddMonths(-1);

        // card_number — digits only, then Luhn (length 14–19 is already guaranteed by Layer 1)
        yield return [With("card_number", "222240534324887a"), "card_number must contain only digits."];
        yield return [With("card_number", "2222405343248878"), "card_number is not a valid card number."];

        // cvv — 3 or 4 digits
        yield return [With("cvv", "12"), "cvv must be 3 or 4 characters long."];
        yield return [With("cvv", "12345"), "cvv must be 3 or 4 characters long."];
        yield return [With("cvv", "12a"), "cvv must contain only digits."];

        // expiry — combined month + year must not be in the past
        yield return
        [
            WithExpiry(lastMonth.Month, lastMonth.Year),
            "expiry_month and expiry_year together must not be in the past."
        ];
    }

    [Theory]
    [MemberData(nameof(ValidDomainPayloads))]
    public async Task ValidRequest_PassesDomainValidation(Dictionary<string, object?> payload)
    {
        var response = await _client.PostAsJsonAsync("/api/payments", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RejectedDomainPayloads))]
    public async Task InvalidRequest_Returns400RejectedWithReason(
        Dictionary<string, object?> payload, string expectedMessage)
    {
        var response = await _client.PostAsJsonAsync("/api/payments", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var rejection = await response.Content.ReadFromJsonAsync<Rejection>();
        Assert.NotNull(rejection);
        Assert.Equal("Rejected", rejection!.Status);
        Assert.Contains(expectedMessage, rejection.Errors);
    }

    [Fact]
    public async Task EveryBrokenRule_IsReportedInOneResponse()
    {
        var lastMonth = DateTime.UtcNow.AddMonths(-1);
        var payload = ValidPayload();
        payload["card_number"] = "222240534324887a"; // non-digit
        payload["cvv"] = "12";                        // too short
        payload["expiry_month"] = lastMonth.Month;    // in the past
        payload["expiry_year"] = lastMonth.Year;

        var response = await _client.PostAsJsonAsync("/api/payments", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var rejection = await response.Content.ReadFromJsonAsync<Rejection>();
        Assert.NotNull(rejection);
        Assert.Equal("Rejected", rejection!.Status);
        Assert.Equal(
            new[]
            {
                "card_number must contain only digits.",
                "cvv must be 3 or 4 characters long.",
                "expiry_month and expiry_year together must not be in the past."
            },
            rejection.Errors);
    }

    [Fact]
    public async Task RejectedResponse_GeneratesNoPaymentId()
    {
        var response = await _client.PostAsJsonAsync("/api/payments", With("cvv", "12"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("id", out _));
    }
}
