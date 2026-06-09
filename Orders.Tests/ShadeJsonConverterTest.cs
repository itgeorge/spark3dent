using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using Orders;

namespace Orders.Tests;

public class ShadeJsonConverterTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new ShadeJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [TestCase("A3.5", Shade.A3_5)]
    [TestCase("a3.5", Shade.A3_5)]
    [TestCase("BL1", Shade.BL1)]
    [TestCase("unspecified", Shade.Unspecified)]
    public void Deserialize_ParsesDentalShadeCodes(string jsonShade, Shade expected)
    {
        var json = $$"""
        {"shade":"{{jsonShade}}"}
        """;

        var result = JsonSerializer.Deserialize<ShadeHolder>(json, JsonOptions);

        Assert.That(result!.Shade, Is.EqualTo(expected));
    }

    [Test]
    public void Serialize_WritesDentalShadeCodes()
    {
        var json = JsonSerializer.Serialize(new ShadeHolder { Shade = Shade.A3_5 }, JsonOptions);

        Assert.That(json, Does.Contain("\"shade\":\"A3.5\""));
    }

    private sealed record ShadeHolder
    {
        public Shade Shade { get; init; }
    }
}
