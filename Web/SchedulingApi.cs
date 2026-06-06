using System.Text.Json;
using System.Text.Json.Serialization;
using Orders;

namespace Web;

public static class SchedulingApi
{
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
                return Results.Json(new { error = "Credentials are required." }, statusCode: 400, options: JsonOptions);
            try
            {
                var result = await auth.LoginAsync(body.ClinicCode, body.Pin, RemoteIp(ctx), UserAgent(ctx), ctx.RequestAborted);
                ctx.Response.Cookies.Append(SchedulingEndpointAuth.AuthCookieName, result.CookieToken, BuildCookieOptions(result.ExpiresAt, ctx));
                return Results.Json(new
                {
                    clinicCode = result.Actor.ClinicCode,
                    clinicName = result.Actor.ClinicDisplayName,
                    credentialLabel = result.Actor.CredentialLabel,
                    role = result.Actor.Role,
                    isTechnician = result.Actor.IsTechnician
                }, JsonOptions);
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
            ctx.Response.Cookies.Delete(SchedulingEndpointAuth.AuthCookieName);
            return Results.Json(new { ok = true }, JsonOptions);
        });

        app.MapGet("/api/scheduling/auth/me", async (HttpContext ctx, SchedulingAuthService auth) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            return Results.Json(new
            {
                clinicCode = actor.ClinicCode,
                clinicName = actor.ClinicDisplayName,
                credentialId = actor.CredentialId,
                credentialLabel = actor.CredentialLabel,
                role = actor.Role,
                isTechnician = actor.IsTechnician
            }, JsonOptions);
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

        app.MapGet("/api/scheduling/clinics", async (HttpContext ctx, SchedulingAuthService auth, ISchedulingConfigProvider provider) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsTechnician) return Results.Json(new { error = "Technician access required." }, statusCode: 403, options: JsonOptions);
            var clinics = provider.Current.Options.Clinics
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(c => new { clinicCode = c.Code, clinicDisplayName = c.DisplayName });
            return Results.Json(new { items = clinics }, JsonOptions);
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
                var created = await orders.CreateOrderAsync(actor, ToDraft(body, body.RequestedDeliveryDate), RemoteIp(ctx), UserAgent(ctx), body.ClinicCode, ctx.RequestAborted);
                return Results.Json(new { order = ToDto(created) }, statusCode: 201, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapGet("/api/scheduling/orders", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, int? limit, string? cursor) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            try
            {
                var page = await orders.ListOrdersPageForActorAsync(actor, limit, cursor, ctx.RequestAborted);
                return Results.Json(ToPageDto(page), JsonOptions);
            }
            catch (FormatException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapGet("/api/scheduling/orders/find", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, string? code, int? limit) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (string.IsNullOrWhiteSpace(code))
                return Results.Json(new { error = "Order code is required." }, statusCode: 400, options: JsonOptions);
            try
            {
                var result = await orders.FindOrderContextForActorAsync(actor, code, limit, ctx.RequestAborted);
                return Results.Json(new
                {
                    order = ToDto(result.Order),
                    listPage = ToPageDto(result.ListPage),
                    result.ListModeRecommended,
                    result.Reason
                }, JsonOptions);
            }
            catch (AmbiguousOrderCodeException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409, options: JsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 404, options: JsonOptions);
            }
        });

        app.MapGet("/api/scheduling/orders/calendar", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, DateOnly? start, DateOnly? end) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (start == null || end == null)
                return Results.Json(new { error = "Calendar start and end query parameters are required." }, statusCode: 400, options: JsonOptions);
            if (start > end)
                return Results.Json(new { error = "Calendar start date must be before or equal to end date." }, statusCode: 400, options: JsonOptions);
            if (end.Value.DayNumber - start.Value.DayNumber + 1 > 93)
                return Results.Json(new { error = "Calendar date range cannot exceed 93 days." }, statusCode: 400, options: JsonOptions);

            var items = await orders.ListCalendarOrdersAsync(actor, start.Value, end.Value, ctx.RequestAborted);
            var days = items
                .GroupBy(o => o.RequestedDeliveryDate)
                .OrderBy(g => g.Key)
                .Select(g => new { date = g.Key, orders = g.Select(ToDto) });
            return Results.Json(new { start = start.Value, end = end.Value, days }, JsonOptions);
        });

        app.MapGet("/api/scheduling/orders/{code}", async (string code, HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var order = await orders.GetOrderByCodeAsync(code, ctx.RequestAborted);
            if (order == null || (!actor.IsTechnician && !string.Equals(order.ClinicCode, actor.ClinicCode, StringComparison.OrdinalIgnoreCase)))
                return Results.Json(new { error = "Order not found." }, statusCode: 404, options: JsonOptions);
            return Results.Json(new { order = ToDto(order) }, JsonOptions);
        });

        app.MapPut("/api/scheduling/orders/{code}", async (string code, HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var body = await ReadJson<UpdateOrderRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var updated = await orders.UpdateOrderAsync(actor, code, ToDraft(body, body.RequestedDeliveryDate), RemoteIp(ctx), UserAgent(ctx), ctx.RequestAborted);
                return Results.Json(new { order = ToDto(updated) }, JsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 404, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapDelete("/api/scheduling/orders/{code}", async (string code, HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            try
            {
                var cancelled = await orders.CancelOrderAsync(actor, code, RemoteIp(ctx), UserAgent(ctx), ctx.RequestAborted);
                return Results.Json(new { order = ToDto(cancelled) }, JsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 404, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
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
        return await SchedulingEndpointAuth.AuthenticateAsync(ctx, auth);
    }

    private static async Task<T?> ReadJson<T>(HttpContext ctx)
    {
        try { return await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, JsonOptions, ctx.RequestAborted); }
        catch (JsonException) { return default; }
    }

    private static OrderDraft ToDraft(OrderShape body, DateOnly deliveryDate)
    {
        var workItems = body.WorkItems?
            .Select(i => new OrderWorkItem(i.ConstructionType, new ToothRange(i.ToothStart, i.ToothEnd)))
            .ToArray() ?? [];
        return new OrderDraft(
            body.CaseName ?? "",
            body.ImpressionDate,
            body.ProductCategory,
            body.Material,
            workItems,
            deliveryDate,
            body.Shade,
            body.Notes);
    }

    private static object ToPageDto(OrderPage page) => new
    {
        items = page.Items.Select(ToDto),
        page.NextCursor,
        page.HasMore
    };

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
        o.Material,
        workItems = o.WorkItems.Select(ToWorkItemDto),
        o.RequestedDeliveryDate,
        o.Status,
        o.Shade,
        o.Notes,
        o.CreatedAt,
        o.UpdatedAt
    };

    private static object ToWorkItemDto(OrderWorkItem item) => new
    {
        item.ConstructionType,
        toothStart = item.ToothStart,
        toothEnd = item.ToothEnd,
        teeth = item.Teeth
    };

    private static string RemoteIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private static string UserAgent(HttpContext ctx) => ctx.Request.Headers.UserAgent.ToString();

    public sealed record LoginRequest(string? ClinicCode, string? Pin);

    public sealed record OrderWorkItemRequest(ConstructionType ConstructionType, int ToothStart, int ToothEnd);

    public abstract record OrderShape
    {
        public string? CaseName { get; init; }
        public DateOnly ImpressionDate { get; init; }
        public ProductCategory ProductCategory { get; init; }
        public Material Material { get; init; }
        public IReadOnlyList<OrderWorkItemRequest>? WorkItems { get; init; }
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
        public string? ClinicCode { get; init; }
    }

    public sealed record UpdateOrderRequest : OrderShape
    {
        public DateOnly RequestedDeliveryDate { get; init; }
    }
}
