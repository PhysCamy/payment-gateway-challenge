using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;

using PaymentGateway.Api.Controllers;

namespace PaymentGateway.Api.Tests;

/// <summary>
/// Schema validation is enforced by the ASP.NET pipeline — model binding, data
/// annotations and JSON deserialisation — before the controller action runs, so these
/// are integration tests driven through <see cref="WebApplicationFactory{T}"/>. A request
/// that clears validation reaches the (stub) action and returns 200; an invalid request is
/// rejected with 400 and a <c>ValidationProblemDetails</c> body whose messages name the
/// offending field and constraint in the API's snake_case vocabulary.
/// </summary>
public class SchemaValidationTests
{
    private readonly HttpClient _client =
        new PaymentGatewayApplicationFactory().CreateClient();

    private sealed record ValidationProblem(
        [property: JsonPropertyName("errors")] Dictionary<string, string[]> Errors);

    private static Dictionary<string, object?> ValidPayload() => new()
    {
        ["card_number"] = "2222405343248877",
        ["expiry_month"] = 4,
        ["expiry_year"] = 2030,
        ["currency"] = "GBP",
        ["amount"] = 100,
        ["cvv"] = "123"
    };

    private static Dictionary<string, object?> Without(string field)
    {
        var payload = ValidPayload();
        payload.Remove(field);
        return payload;
    }

    private static Dictionary<string, object?> With(string field, object? value)
    {
        var payload = ValidPayload();
        payload[field] = value;
        return payload;
    }

    public static IEnumerable<object[]> AcceptedPayloads()
    {
        yield return [ValidPayload()];
        // All-zeros is Luhn-valid at any length, so these exercise the 14/19 length
        // boundary at Layer 1 while still clearing Layer 2 to reach the 200.
        yield return [With("card_number", new string('0', 14))];
        yield return [With("card_number", new string('0', 19))];
        yield return [With("expiry_month", 1)];
        yield return [With("expiry_month", 12)];
        yield return [With("currency", "USD")];
        yield return [With("currency", "GBP")];
        yield return [With("currency", "EUR")];
        yield return [With("amount", 1)];
    }

    /// <summary>Invalid payload, the error key it should surface under, and a substring the message must contain.</summary>
    public static IEnumerable<object[]> RejectedPayloads()
    {
        // card_number — required; length 14–19
        yield return [Without("card_number"), "CardNumber", "card_number is required."];
        yield return [With("card_number", new string('2', 13)), "CardNumber", "card_number must be between 14 and 19 characters long."];
        yield return [With("card_number", new string('2', 20)), "CardNumber", "card_number must be between 14 and 19 characters long."];

        // expiry_month — required; 1–12
        yield return [Without("expiry_month"), "ExpiryMonth", "expiry_month is required."];
        yield return [With("expiry_month", 0), "ExpiryMonth", "expiry_month must be between 1 and 12."];
        yield return [With("expiry_month", 13), "ExpiryMonth", "expiry_month must be between 1 and 12."];

        // expiry_year — required
        yield return [Without("expiry_year"), "ExpiryYear", "expiry_year is required."];

        // currency — required; one of USD, GBP, EUR
        yield return [Without("currency"), "Currency", "currency is required."];
        yield return [With("currency", "JPY"), "$.currency", "currency must be one of: USD, GBP, EUR."];

        // amount — required; >= 1
        yield return [Without("amount"), "Amount", "amount is required."];
        yield return [With("amount", 0), "Amount", "amount must be a positive integer of at least 1."];
        yield return [With("amount", -1), "Amount", "amount must be a positive integer of at least 1."];

        // cvv — required; length 1–50 (empty string fails [Required])
        yield return [Without("cvv"), "Cvv", "cvv is required."];
        yield return [With("cvv", ""), "Cvv", "cvv is required."];

        // unknown fields are rejected, and the offending property is named
        var unknownField = ValidPayload();
        unknownField["foo"] = "bar";
        yield return [unknownField, "$.foo", "could not be mapped"];
    }

    [Theory]
    [MemberData(nameof(AcceptedPayloads))]
    public async Task ValidPayload_PassesSchemaValidation(Dictionary<string, object?> payload)
    {
        var response = await _client.PostAsJsonAsync("/api/payments", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RejectedPayloads))]
    public async Task InvalidPayload_Returns400WithActionableMessage(
        Dictionary<string, object?> payload, string errorKey, string expectedMessage)
    {
        var response = await _client.PostAsJsonAsync("/api/payments", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.NotNull(problem);

        Assert.True(problem!.Errors.ContainsKey(errorKey),
            $"Expected an error under '{errorKey}'. Got: {string.Join(", ", problem.Errors.Keys)}");
        Assert.Contains(problem.Errors[errorKey], message => message.Contains(expectedMessage));
    }
}
