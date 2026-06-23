using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Requests;

public class PostPaymentRequest
{
    [Required(ErrorMessage = "card_number is required.")]
    [StringLength(19, MinimumLength = 14,
        ErrorMessage = "card_number must be between 14 and 19 characters long.")]
    [JsonPropertyName("card_number")]
    public string? CardNumber { get; set; }

    [Required(ErrorMessage = "expiry_month is required.")]
    [Range(1, 12, ErrorMessage = "expiry_month must be between 1 and 12.")]
    [JsonPropertyName("expiry_month")]
    public int? ExpiryMonth { get; set; }

    [Required(ErrorMessage = "expiry_year is required.")]
    [JsonPropertyName("expiry_year")]
    public int? ExpiryYear { get; set; }

    [Required(ErrorMessage = "currency is required.")]
    [JsonPropertyName("currency")]
    public Currency? Currency { get; set; }

    [Required(ErrorMessage = "amount is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "amount must be a positive integer of at least 1.")]
    [JsonPropertyName("amount")]
    public int? Amount { get; set; }

    [Required(ErrorMessage = "cvv is required.")]
    [StringLength(50, MinimumLength = 1,
        ErrorMessage = "cvv must be between 1 and 50 characters long.")]
    [JsonPropertyName("cvv")]
    public string? Cvv { get; set; }
}
