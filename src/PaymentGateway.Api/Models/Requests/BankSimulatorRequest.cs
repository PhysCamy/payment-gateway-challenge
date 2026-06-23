using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Requests;

/// <summary>
/// The payload sent to the bank simulator. This is the wire shape the bank expects, which
/// differs from <see cref="PostPaymentRequest"/>: the expiry month and year are combined
/// into a single <c>MM/YYYY</c> string and the currency is sent as its plain code.
/// </summary>
public sealed record BankSimulatorRequest
{
    [JsonPropertyName("card_number")]
    public required string CardNumber { get; init; }

    [JsonPropertyName("expiry_date")]
    public required string ExpiryDate { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("amount")]
    public required int Amount { get; init; }

    [JsonPropertyName("cvv")]
    public required string Cvv { get; init; }
}
