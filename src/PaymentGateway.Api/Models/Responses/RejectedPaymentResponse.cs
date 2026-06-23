using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Responses;

/// <summary>
/// Body returned for a Layer 2 (domain) validation failure: <c>{ "status": "Rejected",
/// "errors": [...] }</c>. No payment ID is generated and nothing is persisted. The
/// <c>errors</c> array tells the merchant exactly which rules the request broke.
/// </summary>
public sealed record RejectedPaymentResponse(
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors)
{
    [JsonPropertyName("status")]
    public PaymentStatus Status { get; } = PaymentStatus.Rejected;
}
