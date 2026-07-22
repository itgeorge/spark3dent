using System.Text;
using System.Text.Json;

namespace Orders;

public sealed record OrderVisibilityScope(string? ClinicCode, string? MemberId);

public sealed record OrderCursor(DateOnly RequestedDeliveryDate, long CreatedAtUnixTimeMilliseconds, long Id)
{
    public static OrderCursor FromOrder(OrderRecord order) =>
        new(order.RequestedDeliveryDate, order.CreatedAt.ToUnixTimeMilliseconds(), order.Id);
}

public sealed record OrderPage(IReadOnlyList<OrderRecord> Items, string? NextCursor, bool HasMore);

public sealed record OrderFindResult(OrderRecord Order, OrderPage ListPage, bool ListModeRecommended, string? Reason);

public sealed class AmbiguousOrderCodeException : InvalidOperationException
{
    public AmbiguousOrderCodeException(string message) : base(message) { }
}

public static class OrderCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(OrderCursor cursor)
    {
        var json = JsonSerializer.Serialize(cursor, JsonOptions);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    public static OrderCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(cursor.Trim()));
            var decoded = JsonSerializer.Deserialize<OrderCursor>(json, JsonOptions);
            if (decoded == null || decoded.Id <= 0 || decoded.CreatedAtUnixTimeMilliseconds <= 0 || decoded.RequestedDeliveryDate == default)
                throw new FormatException("Invalid order cursor.");
            return decoded;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new FormatException("Invalid order cursor.", ex);
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch
        {
            0 => "",
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid order cursor.")
        };
        return Convert.FromBase64String(s);
    }
}
