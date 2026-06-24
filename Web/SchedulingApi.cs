using System.Text.Json;
using System.Text.Json.Serialization;
using Orders;
using Utilities;

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
            var organizationCode = body?.OrganizationCode ?? body?.ClinicCode;
            if (string.IsNullOrWhiteSpace(organizationCode) || string.IsNullOrWhiteSpace(body?.Pin))
                return Results.Json(new { error = "Credentials are required." }, statusCode: 400, options: JsonOptions);
            try
            {
                var result = await auth.LoginAsync(organizationCode, body.Pin, RemoteIp(ctx), UserAgent(ctx), ctx.RequestAborted);
                ctx.Response.Cookies.Append(SchedulingEndpointAuth.AuthCookieName, result.CookieToken, BuildCookieOptions(result.ExpiresAt, ctx));
                return Results.Json(ToActorDto(result.Actor), JsonOptions);
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
            return Results.Json(ToActorDto(actor), JsonOptions);
        });

        app.MapGet("/api/scheduling/material-options", async (HttpContext ctx, SchedulingAuthService auth, IMaterialSchedulingConfigAdminRepository materialConfigs) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var configuredMaterials = (await materialConfigs.ListAdminAsync(ctx.RequestAborted))
                .Select(c => c.Material)
                .ToHashSet();
            return Results.Json(new { items = BuildMaterialOptionDtos(configuredMaterials) }, JsonOptions);
        });

        app.MapGet("/api/scheduling/config", async (HttpContext ctx, SchedulingAuthService auth, IMaterialSchedulingConfigAdminRepository materialConfigs, ISchedulingCapacityConfigAdminRepository capacityConfigs, IClock clock) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            var materialConfigRows = await materialConfigs.ListAdminAsync(ctx.RequestAborted);
            var configuredMaterials = materialConfigRows.Select(c => c.Material).ToHashSet();
            var materialOptions = BuildMaterialOptionDtos(configuredMaterials);
            var materialSchedulingConfigs = materialConfigRows.Select(ToMaterialSchedulingConfigDto);
            var missingMaterials = materialOptions.Where(x => !x.HasAnyConfig).ToArray();
            var capacityConfigRows = await capacityConfigs.ListAdminAsync(ctx.RequestAborted);
            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.Date);
            var currentCapacityConfigId = capacityConfigRows
                .Where(c => c.ActiveFromDate <= today)
                .OrderByDescending(c => c.ActiveFromDate)
                .ThenByDescending(c => c.Id)
                .Select(c => (long?)c.Id)
                .FirstOrDefault();
            var schedulingCapacityConfigs = capacityConfigRows.Select(c => ToCapacityConfigDto(c, today, currentCapacityConfigId));
            return Results.Json(new { materialOptions, missingMaterials, materialSchedulingConfigs, capacityConfigs = schedulingCapacityConfigs, today }, JsonOptions);
        });

        app.MapPost("/api/scheduling/config/capacity", async (HttpContext ctx, SchedulingAuthService auth, ISchedulingCapacityConfigAdminRepository capacityConfigs, IAuditLog auditLog, IClock clock) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            var body = await ReadJson<SchedulingCapacityConfigCreate>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var created = await capacityConfigs.CreateAsync(body, clock.UtcNow, ctx.RequestAborted);
                await AppendSchedulingConfigAuditAsync(auditLog, clock, ctx, "SchedulingCapacityConfigCreated", "SchedulingCapacityConfig", created.Id.ToString(), created.ActiveFromDate.ToString("yyyy-MM-dd"), new { @new = created });
                return Results.Json(new { capacityConfig = ToCapacityConfigDto(created, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.Date), created.ActiveFromDate <= DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.Date) ? created.Id : (long?)null) }, statusCode: 201, options: JsonOptions);
            }
            catch (DuplicateSchedulingCapacityConfigDateException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapPost("/api/scheduling/config/materials", async (HttpContext ctx, SchedulingAuthService auth, IMaterialSchedulingConfigAdminRepository materialConfigs, IAuditLog auditLog, IClock clock) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            var body = await ReadJson<MaterialSchedulingConfigCreateRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var created = await materialConfigs.CreateAsync(body.Material, new MaterialSchedulingConfigCreate(body.FixedLeadTimeBusinessDays, body.CapacityUnitsPerTooth, body.TeethPerExtraLeadDay), clock.UtcNow, ctx.RequestAborted);
                await AppendSchedulingConfigAuditAsync(auditLog, clock, ctx, "SchedulingMaterialConfigCreated", "SchedulingMaterialConfig", created.Material.ToString(), MaterialOptions.Get(created.Material).Title, new { @new = created });
                return Results.Json(new { materialSchedulingConfig = ToMaterialSchedulingConfigDto(created) }, statusCode: 201, options: JsonOptions);
            }
            catch (MaterialSchedulingConfigAlreadyExistsException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapGet("/api/scheduling/config/materials/{material}/history", async (string material, HttpContext ctx, SchedulingAuthService auth, IMaterialSchedulingConfigAdminRepository materialConfigs, int? offset, int? limit) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            if (!Enum.TryParse<Material>(material, ignoreCase: true, out var parsedMaterial))
                return Results.Json(new { error = "Unknown material." }, statusCode: 404, options: JsonOptions);

            var requestedOffset = Math.Max(0, offset ?? 0);
            var requestedLimit = Math.Clamp(limit ?? 25, 1, 100);
            var rows = await materialConfigs.ListHistoryAsync(parsedMaterial, requestedOffset, requestedLimit + 1, ctx.RequestAborted);
            var items = rows.Take(requestedLimit).Select(ToMaterialSchedulingConfigDto);
            return Results.Json(new { items, nextOffset = rows.Count > requestedLimit ? requestedOffset + requestedLimit : (int?)null }, options: JsonOptions);
        });

        app.MapPut("/api/scheduling/config/materials/{material}", async (string material, HttpContext ctx, SchedulingAuthService auth, IMaterialSchedulingConfigAdminRepository materialConfigs, IAuditLog auditLog, IClock clock) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            if (!Enum.TryParse<Material>(material, ignoreCase: true, out var parsedMaterial))
                return Results.Json(new { error = "Unknown material." }, statusCode: 404, options: JsonOptions);
            var body = await ReadJson<MaterialSchedulingConfigUpdate>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var old = (await materialConfigs.ListAdminAsync(ctx.RequestAborted)).FirstOrDefault(c => c.Material == parsedMaterial);
                var updated = await materialConfigs.UpdateAsync(parsedMaterial, body, clock.UtcNow, ctx.RequestAborted);
                await AppendSchedulingConfigAuditAsync(auditLog, clock, ctx, "SchedulingMaterialConfigUpdated", "SchedulingMaterialConfig", updated.Material.ToString(), MaterialOptions.Get(updated.Material).Title, new { old, @new = updated });
                return Results.Json(new { materialSchedulingConfig = ToMaterialSchedulingConfigDto(updated) }, options: JsonOptions);
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

        app.MapGet("/api/scheduling/clinics", async (HttpContext ctx, SchedulingAuthService auth, ISchedulingIdentityRepository identities) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            var clinics = (await identities.ListClinicsAsync(includeInactive: false, ctx.RequestAborted))
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(ToClinicMetaDto);
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
                var previewOrder = await ResolveDatePreviewOrderAsync(body.OrderCode, actor, orders, ctx.RequestAborted);
                var impressionTimestampUtc = previewOrder?.CreatedAt;
                var excludedOrderId = previewOrder?.Id;
                var minimum = impressionTimestampUtc.HasValue
                    ? await orders.CalculateMinimumDeliveryDateAsync(draft, impressionTimestampUtc.Value, ctx.RequestAborted)
                    : await orders.CalculateMinimumDeliveryDateAsync(draft, ctx.RequestAborted);
                var statuses = impressionTimestampUtc.HasValue
                    ? await orders.GetDateStatusesResultAsync(draft, body.Start, body.End, impressionTimestampUtc.Value, excludedOrderId, ctx.RequestAborted)
                    : await orders.GetDateStatusesResultAsync(draft, body.Start, body.End, ctx.RequestAborted);
                return Results.Json(new { minimumDate = minimum, recommendedDate = statuses.RecommendedDate, dates = statuses.Statuses.Select(s => ToDateStatusDto(s, actor.IsLab)) }, JsonOptions);
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

        app.MapPost("/api/scheduling/orders", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var body = await ReadJson<CreateOrderRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var created = await orders.CreateOrderAsync(actor, ToDraft(body, body.RequestedDeliveryDate), RemoteIp(ctx), UserAgent(ctx), body.ClinicCode, ToDeadlineOverrideRequest(body), ctx.RequestAborted);
                return Results.Json(new { order = ToDto(created) }, statusCode: 201, options: JsonOptions);
            }
            catch (DeadlineOverrideRequiredException ex)
            {
                return Results.Json(ToDeadlineOverrideErrorDto(ex), statusCode: 400, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapGet("/api/scheduling/orders", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, ISchedulingIdentityRepository identities, int? limit, string? cursor) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            try
            {
                var page = await orders.ListOrdersPageForActorAsync(actor, limit, cursor, ctx.RequestAborted);
                Dictionary<string, object>? clinics = null;
                if (actor.IsLab)
                    clinics = await BuildClinicsMetaMapAsync(identities, page.Items, ctx.RequestAborted);
                return Results.Json(ToPageDto(page, clinics), JsonOptions);
            }
            catch (FormatException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapGet("/api/scheduling/orders/find", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, ISchedulingIdentityRepository identities, string? code, int? limit) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (string.IsNullOrWhiteSpace(code))
                return Results.Json(new { error = "Order code is required." }, statusCode: 400, options: JsonOptions);
            try
            {
                var result = await orders.FindOrderContextForActorAsync(actor, code, limit, ctx.RequestAborted);
                Dictionary<string, object>? clinics = null;
                SchedulingClinic? orderClinic = null;
                if (actor.IsLab)
                {
                    clinics = await BuildClinicsMetaMapAsync(identities, result.ListPage.Items.Append(result.Order), ctx.RequestAborted);
                    orderClinic = await identities.GetClinicAsync(result.Order.ClinicCode, includeInactive: false, ctx.RequestAborted);
                }
                return Results.Json(new
                {
                    order = ToDto(result.Order, orderClinic),
                    listPage = ToPageDto(result.ListPage, clinics),
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

        app.MapGet("/api/scheduling/orders/calendar", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, ISchedulingIdentityRepository identities, DateOnly? start, DateOnly? end) =>
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
            Dictionary<string, object>? clinics = null;
            IReadOnlyDictionary<DateOnly, DailyCapacityUsage>? capacityByDate = null;
            if (actor.IsLab)
            {
                clinics = await BuildClinicsMetaMapAsync(identities, items, ctx.RequestAborted);
                capacityByDate = await orders.GetDailyCapacityUsageByDateAsync(start.Value, end.Value, ctx.RequestAborted);
            }
            var days = items
                .GroupBy(o => o.RequestedDeliveryDate)
                .OrderBy(g => g.Key)
                .Select(g => ToCalendarDayDto(g.Key, g, capacityByDate));
            return Results.Json(ToCalendarDto(start.Value, end.Value, days, clinics), JsonOptions);
        });

        app.MapGet("/api/scheduling/non-working-days", async (HttpContext ctx, SchedulingAuthService auth, INonWorkingDayProvider nonWorkingDayProvider, DateOnly? start, DateOnly? end) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (start == null || end == null)
                return Results.Json(new { error = "Non-working day start and end query parameters are required." }, statusCode: 400, options: JsonOptions);
            if (start > end)
                return Results.Json(new { error = "Non-working day start date must be before or equal to end date." }, statusCode: 400, options: JsonOptions);
            if (end.Value.DayNumber - start.Value.DayNumber + 1 > 93)
                return Results.Json(new { error = "Non-working day date range cannot exceed 93 days." }, statusCode: 400, options: JsonOptions);

            var dates = new List<string>();
            for (var year = start.Value.Year; year <= end.Value.Year; year++)
            {
                var days = await nonWorkingDayProvider.GetNonWorkingDaysAsync(year, ctx.RequestAborted);
                foreach (var day in days)
                {
                    if (day >= start && day <= end)
                        dates.Add(day.ToString("yyyy-MM-dd"));
                }
            }

            dates.Sort(StringComparer.Ordinal);
            return Results.Json(new { start, end, dates }, JsonOptions);
        });

        app.MapGet("/api/scheduling/orders/{code}/deadline-recommendation-logs", async (string code, HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, IDeadlineRecommendationLogRepository logs) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            var order = await orders.GetOrderByCodeAsync(code, ctx.RequestAborted);
            if (order == null) return Results.Json(new { error = "Order not found." }, statusCode: 404, options: JsonOptions);
            var items = await logs.ListForOrderAsync(order.Id, ctx.RequestAborted);
            return Results.Json(new { items = items.Select(ToDeadlineRecommendationLogDto) }, JsonOptions);
        });

        app.MapGet("/api/scheduling/orders/{code}/deadline-override-logs", async (string code, HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, IDeadlineOverrideLogRepository logs) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            var order = await orders.GetOrderByCodeAsync(code, ctx.RequestAborted);
            if (order == null) return Results.Json(new { error = "Order not found." }, statusCode: 404, options: JsonOptions);
            var items = await logs.ListForOrderAsync(order.Id, ctx.RequestAborted);
            return Results.Json(new { items = items.Select(ToDeadlineOverrideLogDto) }, JsonOptions);
        });

        app.MapGet("/api/scheduling/orders/{code}", async (string code, HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, ISchedulingIdentityRepository identities) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var order = await orders.GetOrderByCodeAsync(code, ctx.RequestAborted);
            if (order == null || (!actor.IsLab && !string.Equals(order.ClinicCode, actor.OrganizationCode, StringComparison.OrdinalIgnoreCase)))
                return Results.Json(new { error = "Order not found." }, statusCode: 404, options: JsonOptions);
            SchedulingClinic? liveClinic = null;
            if (actor.IsLab)
                liveClinic = await identities.GetClinicAsync(order.ClinicCode, includeInactive: false, ctx.RequestAborted);
            return Results.Json(new { order = ToDto(order, liveClinic) }, JsonOptions);
        });

        app.MapPut("/api/scheduling/orders/{code}", async (string code, HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var body = await ReadJson<UpdateOrderRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var updated = await orders.UpdateOrderAsync(actor, code, ToDraft(body, body.RequestedDeliveryDate), RemoteIp(ctx), UserAgent(ctx), ToDeadlineOverrideRequest(body), ctx.RequestAborted);
                return Results.Json(new { order = ToDto(updated) }, JsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 404, options: JsonOptions);
            }
            catch (DeadlineOverrideRequiredException ex)
            {
                return Results.Json(ToDeadlineOverrideErrorDto(ex), statusCode: 400, options: JsonOptions);
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

    private static async Task<OrderRecord?> ResolveDatePreviewOrderAsync(
        string? orderCode,
        AuthenticatedActor actor,
        SchedulingOrderService orders,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderCode)) return null;

        var order = await orders.GetOrderByCodeAsync(orderCode.Trim(), ct);
        if (order == null || (!actor.IsLab && !string.Equals(order.ClinicCode, actor.OrganizationCode, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException("Order not found.");
        return order;
    }

    private static DeadlineOverrideRequest? ToDeadlineOverrideRequest(OrderShape body) =>
        body.ConfirmDeadlineOverride || !string.IsNullOrWhiteSpace(body.DeadlineOverrideReason)
            ? new DeadlineOverrideRequest(body.ConfirmDeadlineOverride, body.DeadlineOverrideReason)
            : null;

    private static object ToDeadlineOverrideErrorDto(DeadlineOverrideRequiredException ex) => new
    {
        error = ex.Message,
        overrideAllowed = ex.OverrideAllowed,
        failedRules = ex.FailedRules.Select(r => r.ToString()),
        recommendedDate = ex.RecommendedDate
    };

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
            body.Notes,
            body.ColorNote);
    }

    private static object ToPageDto(OrderPage page, Dictionary<string, object>? clinics = null)
    {
        if (clinics == null || clinics.Count == 0)
        {
            return new
            {
                items = page.Items.Select(o => ToDto(o)),
                page.NextCursor,
                page.HasMore
            };
        }

        return new
        {
            items = page.Items.Select(o => ToDto(o)),
            page.NextCursor,
            page.HasMore,
            clinics
        };
    }

    private static object ToCalendarDto(DateOnly start, DateOnly end, IEnumerable<object> days, Dictionary<string, object>? clinics)
    {
        if (clinics == null || clinics.Count == 0)
            return new { start, end, days };
        return new { start, end, days, clinics };
    }

    private static object ToDateStatusDto(DeliveryDateStatus status, bool includeExactCapacity)
    {
        var capacityLoadLevel = CapacityLoadLevel(status);
        if (includeExactCapacity)
        {
            return new
            {
                status.Date,
                status.IsClosed,
                status.IsFirstBusinessDayAfterClosure,
                status.IsBeforeMinimum,
                status.IsSelectable,
                status.Reason,
                status.IsDailyCapacityExceeded,
                status.IsWeeklyCapacityExceeded,
                capacityLoadLevel,
                status.OrderCapacityUnits,
                status.ExistingDailyCapacityUsed,
                status.ExistingWeeklyCapacityUsed,
                status.DailyCapacityLimit,
                status.WeeklyCapacityLimit
            };
        }

        return new
        {
            status.Date,
            status.IsClosed,
            status.IsFirstBusinessDayAfterClosure,
            status.IsBeforeMinimum,
            status.IsSelectable,
            status.Reason,
            status.IsDailyCapacityExceeded,
            status.IsWeeklyCapacityExceeded,
            capacityLoadLevel
        };
    }

    private static string? CapacityLoadLevel(DeliveryDateStatus status)
    {
        if (!status.ExistingDailyCapacityUsed.HasValue || !status.DailyCapacityLimit.HasValue || status.DailyCapacityLimit.Value <= 0m)
            return null;
        var ratio = status.ExistingDailyCapacityUsed.Value / status.DailyCapacityLimit.Value;
        return ratio < 0.4m ? "low" : ratio < 0.8m ? "medium" : "high";
    }

    private static object ToCalendarDayDto(DateOnly date, IEnumerable<OrderRecord> orders, IReadOnlyDictionary<DateOnly, DailyCapacityUsage>? capacityByDate)
    {
        var orderDtos = orders.Select(o => ToDto(o));
        if (capacityByDate != null && capacityByDate.TryGetValue(date, out var capacity))
            return new { date, orders = orderDtos, capacity = ToDailyCapacityDto(capacity) };
        return new { date, orders = orderDtos };
    }

    private static object ToDailyCapacityDto(DailyCapacityUsage capacity) => new
    {
        capacity.Used,
        capacity.Limit
    };

    private static object ToClinicMetaDto(SchedulingClinic clinic) => new
    {
        clinicCode = clinic.Code,
        clinicDisplayName = clinic.DisplayName,
        clinicDisplayColor = clinic.DisplayColor,
        linkedClientNickname = clinic.LinkedClientNickname
    };

    private static async Task<Dictionary<string, object>> BuildClinicsMetaMapAsync(
        ISchedulingIdentityRepository identities,
        IEnumerable<OrderRecord> orders,
        CancellationToken ct)
    {
        var needed = orders
            .Select(o => o.ClinicCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (needed.Count == 0)
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var clinics = await identities.ListClinicsAsync(includeInactive: false, ct);
        return clinics
            .Where(c => needed.Contains(c.Code))
            .ToDictionary(c => c.Code, c => ToClinicMetaDto(c), StringComparer.OrdinalIgnoreCase);
    }

    private static object ToDto(OrderRecord o, SchedulingClinic? liveClinic = null)
    {
        if (liveClinic == null)
        {
            return new
            {
                o.Id,
                o.OrderCode,
                shortenedOrderCode = DescriptiveOrderCodeGenerator.ToShortenedCode(o.OrderCode),
                o.ClinicCode,
                o.ClinicDisplayName,
                o.MemberId,
                o.MemberLabel,
                o.CaseName,
                o.ImpressionDate,
                o.ProductCategory,
                o.Material,
                workItems = o.WorkItems.Select(ToWorkItemDto),
                o.RequestedDeliveryDate,
                o.Status,
                o.Shade,
                o.Notes,
                o.ColorNote,
                o.CalculatedCapacityUnits,
                o.CreatedAt,
                o.UpdatedAt
            };
        }

        return new
        {
            o.Id,
            o.OrderCode,
            shortenedOrderCode = DescriptiveOrderCodeGenerator.ToShortenedCode(o.OrderCode),
            o.ClinicCode,
            o.ClinicDisplayName,
            clinicDisplayColor = liveClinic.DisplayColor,
            linkedClientNickname = liveClinic.LinkedClientNickname,
            o.MemberId,
            o.MemberLabel,
            o.CaseName,
            o.ImpressionDate,
            o.ProductCategory,
            o.Material,
            workItems = o.WorkItems.Select(ToWorkItemDto),
            o.RequestedDeliveryDate,
            o.Status,
            o.Shade,
            o.Notes,
            o.ColorNote,
            o.CalculatedCapacityUnits,
            o.CreatedAt,
            o.UpdatedAt
        };
    }

    private static object ToWorkItemDto(OrderWorkItem item) => new
    {
        item.ConstructionType,
        toothStart = item.ToothStart,
        toothEnd = item.ToothEnd,
        teeth = item.Teeth
    };

    private static object ToActorDto(AuthenticatedActor actor) => new
    {
        organizationType = actor.OrganizationType.ToString().ToLowerInvariant(),
        organizationCode = actor.OrganizationCode,
        organizationName = actor.OrganizationName,
        memberId = actor.MemberId,
        memberLabel = actor.MemberLabel,
        isLab = actor.IsLab,
        isClinic = actor.IsClinic
    };

    private static object ToMaterialSchedulingConfigDto(MaterialSchedulingConfigAdminRecord c) => new
    {
        c.Material,
        c.FixedLeadTimeBusinessDays,
        c.CapacityUnitsPerTooth,
        c.TeethPerExtraLeadDay,
        c.ActiveFromDate
    };

    private static MaterialOptionDto[] BuildMaterialOptionDtos(IReadOnlySet<Material> configuredMaterials) =>
        MaterialOptions.All
            .OrderBy(x => x.SortOrder)
            .Select(x => new MaterialOptionDto(
                x.Material,
                x.Title,
                x.Description,
                configuredMaterials.Contains(x.Material)))
            .ToArray();

    private static object ToCapacityConfigDto(SchedulingCapacityConfigAdminRecord c, DateOnly today, long? currentId)
    {
        var status = currentId == c.Id ? "current" : c.ActiveFromDate > today ? "future" : "historical";
        return new
        {
            c.Id,
            c.ActiveFromDate,
            c.DailyCapacityUnits,
            c.WeeklyCapacityUnits,
            c.CreatedAt,
            c.UpdatedAt,
            Status = status,
            IsCurrent = currentId == c.Id
        };
    }

    private static Task AppendSchedulingConfigAuditAsync(IAuditLog auditLog, IClock clock, HttpContext ctx, string operation, string entityType, string entityId, string? entityDisplay, object metadata)
    {
        var actor = SchedulingEndpointAuth.CurrentActor(ctx);
        var auditEvent = new AuditEvent(
            0,
            "Scheduling",
            operation,
            entityType,
            entityId,
            entityDisplay,
            actor?.OrganizationType.ToString() ?? "Unknown",
            actor?.OrganizationCode,
            actor?.MemberId,
            actor?.MemberLabel,
            actor?.SessionId,
            clock.UtcNow,
            RemoteIp(ctx),
            string.IsNullOrWhiteSpace(UserAgent(ctx)) ? null : UserAgent(ctx),
            JsonSerializer.Serialize(metadata, JsonOptions));
        return auditLog.AppendAsync(auditEvent, ctx.RequestAborted);
    }

    private static object ToDeadlineRecommendationLogDto(DeadlineRecommendationLog log) => new
    {
        log.Id,
        log.OrderId,
        log.OrderCode,
        log.CreatedAtUtc,
        log.CreatedByOrganizationType,
        log.CreatedByOrganizationCode,
        log.CreatedByMemberId,
        log.CreatedByMemberLabel,
        log.OrderCreatedAtUtc,
        log.EffectiveIntakeBusinessDate,
        log.CutoffTimeUsed,
        log.Material,
        log.ToothCount,
        log.LeadTimeBusinessDaysUsed,
        log.FixedLeadTimeBusinessDaysUsed,
        log.ExtraLeadTimeBusinessDaysUsed,
        log.TeethPerExtraLeadDayUsed,
        log.CapacityUnitsPerToothUsed,
        log.CalculatedOrderCapacityUnits,
        log.MinimumDeadlineDateFromLeadTime,
        log.FinalRecommendedDeadlineDate,
        log.SelectedDeadlineDate,
        log.SearchStartedAtDate,
        log.SearchEndedAtDate,
        log.SearchLimitDate,
        log.ResultStatus,
        log.FailureReason,
        log.CandidateChecksJson,
        log.ConfigSnapshotJson
    };

    private static object ToDeadlineOverrideLogDto(DeadlineOverrideLog log) => new
    {
        log.Id,
        log.OrderId,
        log.OrderCode,
        log.CreatedAtUtc,
        log.CreatedByOrganizationType,
        log.CreatedByOrganizationCode,
        log.CreatedByMemberId,
        log.CreatedByMemberLabel,
        log.SelectedDeadlineDate,
        log.SystemRecommendedDeadlineDate,
        log.MinimumDeadlineDate,
        log.OrderCapacityUnits,
        log.RulesBypassedJson,
        log.OverrideReason,
        log.RecommendationLogId,
        log.ExistingDailyCapacityUsed,
        log.ExistingWeeklyCapacityUsed,
        log.DailyCapacityLimitUsed,
        log.WeeklyCapacityLimitUsed,
        log.DailyCapacityAfterOverride,
        log.WeeklyCapacityAfterOverride,
        log.CalendarReason
    };

    private static string RemoteIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private static string UserAgent(HttpContext ctx) => ctx.Request.Headers.UserAgent.ToString();

    public sealed record LoginRequest(string? OrganizationCode, string? ClinicCode, string? Pin);

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
        public string? ColorNote { get; init; }
        public bool ConfirmDeadlineOverride { get; init; }
        public string? DeadlineOverrideReason { get; init; }
    }

    public sealed record DateAvailabilityRequest : OrderShape
    {
        public DateOnly Start { get; init; }
        public DateOnly End { get; init; }
        public string? OrderCode { get; init; }
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

    public sealed record MaterialSchedulingConfigCreateRequest(
        Material Material,
        int FixedLeadTimeBusinessDays,
        decimal CapacityUnitsPerTooth,
        int? TeethPerExtraLeadDay);

    public sealed record MaterialOptionDto(
        Material Material,
        string Title,
        string Description,
        bool HasAnyConfig);
}
