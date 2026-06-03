using System.Text.Json;
using System.Text.Json.Serialization;
using Orders;

namespace Web;

public static class SchedulingApi
{
    private const string AuthCookieName = "s3d_order_session";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new ShadeJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void MapRoutes(WebApplication app)
    {
        app.MapPost("/api/scheduling/auth/login", async (HttpContext ctx, SchedulingAuthService auth) =>
        {
            var body = await ReadJson<LoginRequest>(ctx);
            if (body == null || string.IsNullOrWhiteSpace(body.ClinicCode) || string.IsNullOrWhiteSpace(body.Pin))
                return Results.Json(new { error = "clinicCode and pin are required." }, statusCode: 400, options: JsonOptions);
            try
            {
                var result = await auth.LoginAsync(body.ClinicCode, body.Pin, RemoteIp(ctx), UserAgent(ctx), ctx.RequestAborted);
                ctx.Response.Cookies.Append(AuthCookieName, result.CookieToken, BuildCookieOptions(result.ExpiresAt, ctx));
                return Results.Json(new { clinicCode = result.Actor.ClinicCode, clinicName = result.Actor.ClinicDisplayName, credentialLabel = result.Actor.CredentialLabel }, JsonOptions);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 401, options: JsonOptions);
            }
        });

        app.MapPost("/api/scheduling/auth/logout", async (HttpContext ctx, SchedulingAuthService auth) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            await auth.LogoutAsync(actor.SessionId, ctx.RequestAborted);
            ctx.Response.Cookies.Delete(AuthCookieName);
            return Results.Json(new { ok = true }, JsonOptions);
        });

        app.MapGet("/api/scheduling/auth/me", async (HttpContext ctx, SchedulingAuthService auth) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            return Results.Json(new { clinicCode = actor.ClinicCode, clinicName = actor.ClinicDisplayName, credentialId = actor.CredentialId, credentialLabel = actor.CredentialLabel }, JsonOptions);
        });

        app.MapGet("/api/scheduling/config", async (HttpContext ctx, SchedulingAuthService auth, ISchedulingConfigProvider provider) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var rules = provider.Current.Options.WorkRules.Select(r => new
            {
                r.ProductCategory,
                r.WorkType,
                r.Material,
                r.ConstructionType,
                r.MinBusinessDays
            });
            return Results.Json(new { rules, defaultMinBusinessDays = provider.Current.Options.DefaultMinBusinessDays }, JsonOptions);
        });

        app.MapPost("/api/scheduling/dates", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var body = await ReadJson<DateAvailabilityRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var draft = ToDraft(body, body.Start);
                var minimum = await orders.CalculateMinimumDeliveryDateAsync(draft, ctx.RequestAborted);
                var statuses = await orders.GetDateStatusesAsync(draft, body.Start, body.End, ctx.RequestAborted);
                return Results.Json(new { minimumDate = minimum, dates = statuses }, JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapPost("/api/scheduling/orders", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var body = await ReadJson<CreateOrderRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var created = await orders.CreateOrderAsync(actor, ToDraft(body, body.RequestedDeliveryDate), RemoteIp(ctx), UserAgent(ctx), ctx.RequestAborted);
                return Results.Json(new { order = ToDto(created) }, statusCode: 201, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapGet("/api/scheduling/orders/{code}", async (string code, HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var order = await orders.GetOrderByCodeAsync(code, ctx.RequestAborted);
            if (order == null || !string.Equals(order.ClinicCode, actor.ClinicCode, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "Order not found." }, statusCode: 404, options: JsonOptions);
            return Results.Json(new { order = ToDto(order) }, JsonOptions);
        });

        app.MapGet("/api/scheduling/technician/orders", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, int? limit) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var items = await orders.ListOrdersAsync(limit ?? 100, ctx.RequestAborted);
            return Results.Json(new { items = items.Select(ToDto) }, JsonOptions);
        });
    }

    private static CookieOptions BuildCookieOptions(DateTimeOffset expires, HttpContext ctx) => new()
    {
        HttpOnly = true,
        Secure = ctx.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Expires = expires,
        Path = "/"
    };

    private static async Task<AuthenticatedActor?> RequireActor(HttpContext ctx, SchedulingAuthService auth)
    {
        ctx.Request.Cookies.TryGetValue(AuthCookieName, out var token);
        return await auth.AuthenticateAsync(token, ctx.RequestAborted);
    }

    private static async Task<T?> ReadJson<T>(HttpContext ctx)
    {
        try { return await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, JsonOptions, ctx.RequestAborted); }
        catch (JsonException) { return default; }
    }

    private static OrderDraft ToDraft(OrderShape body, DateOnly deliveryDate) => new(
        body.CaseName ?? "",
        body.ImpressionDate,
        body.ProductCategory,
        body.WorkType,
        body.Material,
        body.ConstructionType,
        new ToothRange(body.ToothStart, body.ToothEnd),
        deliveryDate,
        body.Shade,
        body.Notes);

    private static object ToDto(OrderRecord o) => new
    {
        o.Id,
        o.OrderCode,
        shortenedOrderCode = DescriptiveOrderCodeGenerator.ToShortenedCode(o.OrderCode),
        o.ClinicCode,
        o.ClinicDisplayName,
        o.CredentialId,
        o.CredentialLabel,
        o.CaseName,
        o.ImpressionDate,
        o.ProductCategory,
        o.WorkType,
        o.Material,
        o.ConstructionType,
        o.ToothStart,
        o.ToothEnd,
        o.AbutmentTeeth,
        o.RequestedDeliveryDate,
        o.Status,
        o.Shade,
        o.Notes,
        o.CreatedAt
    };

    private static string RemoteIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private static string UserAgent(HttpContext ctx) => ctx.Request.Headers.UserAgent.ToString();

    public sealed record LoginRequest(string? ClinicCode, string? Pin);

    public abstract record OrderShape
    {
        public string? CaseName { get; init; }
        public DateOnly ImpressionDate { get; init; }
        public ProductCategory ProductCategory { get; init; }
        public WorkType WorkType { get; init; }
        public Material Material { get; init; }
        public ConstructionType ConstructionType { get; init; }
        public int ToothStart { get; init; }
        public int ToothEnd { get; init; }
        public Shade Shade { get; init; }
        public string? Notes { get; init; }
    }

    public sealed record DateAvailabilityRequest : OrderShape
    {
        public DateOnly Start { get; init; }
        public DateOnly End { get; init; }
    }

    public sealed record CreateOrderRequest : OrderShape
    {
        public DateOnly RequestedDeliveryDate { get; init; }
    }
}
