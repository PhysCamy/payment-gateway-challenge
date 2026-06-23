using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models;

/// <summary>
/// Deserialises <see cref="Currency"/> from its string form, rejecting unknown values
/// with a user-facing message that lists the supported codes rather than leaking the
/// default converter's .NET type names.
/// </summary>
public class CurrencyJsonConverter : JsonConverter<Currency>
{
    private static readonly string[] Supported = Enum.GetNames<Currency>();

    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

        var match = Supported.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return Enum.Parse<Currency>(match);
        }

        throw new JsonException($"currency must be one of: {string.Join(", ", Supported)}.");
    }

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
