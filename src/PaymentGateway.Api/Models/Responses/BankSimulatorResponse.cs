using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Responses;

/// <summary>
/// The bank simulator's authorization decision for a forwarded payment.
/// </summary>
public sealed record BankSimulatorResponse
{
    [JsonPropertyName("authorized")]
    public bool Authorized { get; init; }

    [JsonPropertyName("authorization_code")]
    public string? AuthorizationCode { get; init; }
}
