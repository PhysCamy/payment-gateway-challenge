using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Responses;

public class PostPaymentResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("status")]
    public PaymentStatus Status { get; set; }

    [JsonPropertyName("last_four_digits")]
    public string LastFourDigits { get; set; } = string.Empty;

    [JsonPropertyName("expiry_month")]
    public int ExpiryMonth { get; set; }

    [JsonPropertyName("expiry_year")]
    public int ExpiryYear { get; set; }

    [JsonPropertyName("currency")]
    public Currency Currency { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}
