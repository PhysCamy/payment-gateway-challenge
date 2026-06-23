using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using PaymentGateway.Api.Controllers;

namespace PaymentGateway.Api.Tests.Integration;

/// <summary>
/// Confirms the controller wiring that turns a Layer 2 (domain) validation failure into the
/// merchant-facing response: <c>400</c> with <c>{ "status": "Rejected", "errors": [...] }</c>
/// and no payment id. The domain rules themselves are exercised in isolation by
/// <c>PostPaymentRequestValidatorTests</c>; this only proves the action maps a rejection
/// onto that envelope, so a single representative invalid request is enough.
/// </summary>
public class PaymentRejectionTests
{
    private readonly HttpClient _client;

    public PaymentRejectionTests()
    {
        _client = new PaymentGatewayApplicationFactory().CreateClient();
        _client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
    }

    [Fact]
    public async Task DomainInvalidRequest_Returns400RejectedEnvelopeWithoutId()
    {
        var payload = new Dictionary<string, object?>
        {
            ["card_number"] = "2222405343248877",
            ["expiry_month"] = 4,
            ["expiry_year"] = DateTime.UtcNow.Year + 1,
            ["currency"] = "GBP",
            ["amount"] = 100,
            ["cvv"] = "12" // too short — fails domain validation
        };

        var response = await _client.PostAsJsonAsync("/api/payments", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal("Rejected", root.GetProperty("status").GetString());
        Assert.Contains(
            "cvv must be 3 or 4 characters long.",
            root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
        Assert.False(root.TryGetProperty("id", out _));
    }
}
