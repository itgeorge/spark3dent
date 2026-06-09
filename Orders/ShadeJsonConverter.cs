using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orders;

public sealed class ShadeJsonConverter : JsonConverter<Shade>
{
    public override Shade Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Shade must be a string.");

        var code = reader.GetString();
        if (ShadeCodes.TryParse(code, out var shade))
            return shade;

        throw new JsonException($"Unknown shade code '{code}'.");
    }

    public override void Write(Utf8JsonWriter writer, Shade value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ShadeCodes.ToDisplayCode(value));
}
