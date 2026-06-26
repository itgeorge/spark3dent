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
            var today = ToLabLocalDate(clock.UtcNow);
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
                var today = ToLabLocalDate(clock.UtcNow);
                var old = (await capacityConfigs.ListAdminAsync(ctx.RequestAborted)).FirstOrDefault(c => c.ActiveFromDate == body.ActiveFromDate);
                var saved = await capacityConfigs.CreateAsync(body, clock.UtcNow, ctx.RequestAborted);
                var wasUpdated = old != null;
                var operation = wasUpdated ? "SchedulingCapacityConfigUpdated" : "SchedulingCapacityConfigCreated";
                await AppendSchedulingConfigAuditAsync(auditLog, clock, ctx, operation, "SchedulingCapacityConfig", saved.Id.ToString(), saved.ActiveFromDate.ToString("yyyy-MM-dd"), new { old, @new = saved });
                var rows = await capacityConfigs.ListAdminAsync(ctx.RequestAborted);
                var currentCapacityConfigId = rows
                    .Where(c => c.ActiveFromDate <= today)
                    .OrderByDescending(c => c.ActiveFromDate)
                    .ThenByDescending(c => c.Id)
                    .Select(c => (long?)c.Id)
                    .FirstOrDefault();
                return Results.Json(new { capacityConfig = ToCapacityConfigDto(saved, today, currentCapacityConfigId) }, statusCode: wasUpdated ? 200 : 201, options: JsonOptions);
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

        app.MapGet("/api/scheduling/config/lab-offdays", async (HttpContext ctx, SchedulingAuthService auth, ILabOffdayRepository labOffdays, DateOnly? start, DateOnly? end) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            if (start == null || end == null)
                return Results.Json(new { error = "Lab offday start and end query parameters are required." }, statusCode: 400, options: JsonOptions);
            if (start > end)
                return Results.Json(new { error = "Lab offday start date must be before or equal to end date." }, statusCode: 400, options: JsonOptions);
            if (end.Value.DayNumber - start.Value.DayNumber + 1 > 370)
                return Results.Json(new { error = "Lab offday date range cannot exceed 370 days." }, statusCode: 400, options: JsonOptions);

            var rows = await labOffdays.ListIntersectingAsync(start.Value, end.Value, ctx.RequestAborted);
            var dates = ExpandLabOffdayDates(rows, start.Value, end.Value).Select(d => d.ToString("yyyy-MM-dd")).ToArray();
            return Results.Json(new { start, end, items = rows.Select(ToLabOffdayDto), dates }, JsonOptions);
        });

        app.MapPost("/api/scheduling/config/lab-offdays", async (HttpContext ctx, SchedulingAuthService auth, ILabOffdayRepository labOffdays, IAuditLog auditLog, IClock clock) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            var body = await ReadJson<LabOffdayCreate>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var created = await labOffdays.CreateAsync(body, clock.UtcNow, ctx.RequestAborted);
                await AppendSchedulingConfigAuditAsync(auditLog, clock, ctx, "SchedulingLabOffdayCreated", "SchedulingLabOffday", created.Id.ToString(), LabOffdayDisplay(created), new { @new = created });
                return Results.Json(new { labOffday = ToLabOffdayDto(created) }, statusCode: 201, options: JsonOptions);
            }
            catch (LabOffdayOverlapException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapPut("/api/scheduling/config/lab-offdays/{id:long}", async (long id, HttpContext ctx, SchedulingAuthService auth, ILabOffdayRepository labOffdays, IAuditLog auditLog, IClock clock) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            var body = await ReadJson<LabOffdayUpdate>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var old = (await labOffdays.ListAllAsync(ctx.RequestAborted)).FirstOrDefault(x => x.Id == id);
                var updated = await labOffdays.UpdateAsync(id, body, clock.UtcNow, ctx.RequestAborted);
                await AppendSchedulingConfigAuditAsync(auditLog, clock, ctx, "SchedulingLabOffdayUpdated", "SchedulingLabOffday", updated.Id.ToString(), LabOffdayDisplay(updated), new { old, @new = updated });
                return Results.Json(new { labOffday = ToLabOffdayDto(updated) }, options: JsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 404, options: JsonOptions);
            }
            catch (LabOffdayOverlapException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409, options: JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapDelete("/api/scheduling/config/lab-offdays/{id:long}", async (long id, HttpContext ctx, SchedulingAuthService auth, ILabOffdayRepository labOffdays, IAuditLog auditLog, IClock clock) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            if (!actor.IsLab) return Results.Json(new { error = "Lab access required." }, statusCode: 403, options: JsonOptions);
            try
            {
                var old = (await labOffdays.ListAllAsync(ctx.RequestAborted)).FirstOrDefault(x => x.Id == id);
                await labOffdays.DeleteAsync(id, ctx.RequestAborted);
                await AppendSchedulingConfigAuditAsync(auditLog, clock, ctx, "SchedulingLabOffdayDeleted", "SchedulingLabOffday", id.ToString(), old == null ? id.ToString() : LabOffdayDisplay(old), new { old });
                return Results.Json(new { ok = true }, options: JsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 404, options: JsonOptions);
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

        app.MapPost("/api/scheduling/reservations/dates", async (HttpContext ctx, SchedulingAuthService auth, SchedulingReservationService reservations) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var body = await ReadJson<ReservationDateAvailabilityRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var draft = ToReservationDraft(body, body.RequestedDeliveryDate == default ? body.Start : body.RequestedDeliveryDate);
                var statuses = await reservations.GetDateStatusesResultAsync(draft, body.Start, body.End, body.ReservationId, ctx.RequestAborted);
                return Results.Json(new { minimumDate = statuses.MinimumDate, recommendedDate = statuses.RecommendedDate, dates = statuses.Statuses.Select(s => ToDateStatusDto(s, actor.IsLab)) }, JsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions);
            }
        });

        app.MapPost("/api/scheduling/reservations", async (HttpContext ctx, SchedulingAuthService auth, SchedulingReservationService reservations) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var body = await ReadJson<CreateReservationRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var created = await reservations.CreateReservationAsync(actor, ToReservationDraft(body, body.RequestedDeliveryDate), RemoteIp(ctx), UserAgent(ctx), body.ClinicCode, ToDeadlineOverrideRequest(body), ctx.RequestAborted);
                return Results.Json(new { reservation = ToDto(created) }, statusCode: 201, options: JsonOptions);
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

        app.MapGet("/api/scheduling/reservations", async (HttpContext ctx, SchedulingAuthService auth, SchedulingReservationService reservations, ISchedulingIdentityRepository identities, int? limit) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var items = await reservations.ListActiveReservationsForActorAsync(actor, limit ?? 100, ctx.RequestAborted);
            Dictionary<string, object>? clinics = null;
            if (actor.IsLab)
                clinics = await BuildClinicsMetaMapAsync(identities, items.Select(r => r.ClinicCode), ctx.RequestAborted);
            return Results.Json(new { items = items.Select(r => ToDto(r)), clinics }, JsonOptions);
        });

        app.MapGet("/api/scheduling/reservations/{id:long}", async (long id, HttpContext ctx, SchedulingAuthService auth, SchedulingReservationService reservations, ISchedulingIdentityRepository identities) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            try
            {
                var reservation = await reservations.GetReservationForActorAsync(actor, id, ctx.RequestAborted);
                SchedulingClinic? liveClinic = null;
                if (actor.IsLab)
                    liveClinic = await identities.GetClinicAsync(reservation.ClinicCode, includeInactive: false, ctx.RequestAborted);
                return Results.Json(new { reservation = ToDto(reservation, liveClinic) }, JsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 404, options: JsonOptions);
            }
        });

        app.MapPut("/api/scheduling/reservations/{id:long}", async (long id, HttpContext ctx, SchedulingAuthService auth, SchedulingReservationService reservations) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            var body = await ReadJson<UpdateReservationRequest>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try
            {
                var updated = await reservations.UpdateReservationAsync(actor, id, ToReservationDraft(body, body.RequestedDeliveryDate), RemoteIp(ctx), UserAgent(ctx), ToDeadlineOverrideRequest(body), ctx.RequestAborted);
                return Results.Json(new { reservation = ToDto(updated) }, JsonOptions);
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

        app.MapDelete("/api/scheduling/reservations/{id:long}", async (long id, HttpContext ctx, SchedulingAuthService auth, SchedulingReservationService reservations) =>
        {
            var actor = await RequireActor(ctx, auth);
            if (actor == null) return Results.Json(new { error = "Not authenticated." }, statusCode: 401, options: JsonOptions);
            try
            {
                var cancelled = await reservations.CancelReservationAsync(actor, id, RemoteIp(ctx), UserAgent(ctx), ctx.RequestAborted);
                return Results.Json(new { reservation = ToDto(cancelled) }, JsonOptions);
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

        app.MapGet("/api/scheduling/orders/calendar", async (HttpContext ctx, SchedulingAuthService auth, SchedulingOrderService orders, SchedulingReservationService reservations, ISchedulingIdentityRepository identities, DateOnly? start, DateOnly? end) =>
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
            var reservationItems = await reservations.ListCalendarReservationsAsync(actor, start.Value, end.Value, ctx.RequestAborted);
            Dictionary<string, object>? clinics = null;
            IReadOnlyDictionary<DateOnly, DailyCapacityUsage>? capacityByDate = null;
            IReadOnlyDictionary<DateOnly, WeeklyCapacityUsage>? weeklyCapacityByWeekEnd = null;
            if (actor.IsLab)
            {
                clinics = await BuildClinicsMetaMapAsync(identities, items.Select(o => o.ClinicCode).Concat(reservationItems.Select(r => r.ClinicCode)), ctx.RequestAborted);
                capacityByDate = await orders.GetDailyCapacityUsageByDateAsync(start.Value, end.Value, ctx.RequestAborted);
                weeklyCapacityByWeekEnd = await orders.GetWeeklyCapacityUsageByWeekEndAsync(start.Value, end.Value, ctx.RequestAborted);
            }
            var ordersByDate = items
                .GroupBy(o => o.RequestedDeliveryDate)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderRecord>)g.ToList());
            var reservationsByDeliveryDate = reservationItems
                .GroupBy(r => r.RequestedDeliveryDate)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ReservationRecord>)g.ToList());
            var reservationsByImpressionDate = reservationItems
                .GroupBy(r => r.ImpressionDate)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ReservationRecord>)g.ToList());
            var dayDates = ordersByDate.Keys
                .Concat(reservationsByDeliveryDate.Keys)
                .Concat(reservationsByImpressionDate.Keys)
                .Concat(weeklyCapacityByWeekEnd?.Keys ?? [])
                .Distinct()
                .OrderBy(d => d);
            var days = dayDates.Select(date => ToCalendarDayDto(date, ordersByDate.GetValueOrDefault(date) ?? [], reservationsByDeliveryDate.GetValueOrDefault(date) ?? [], reservationsByImpressionDate.GetValueOrDefault(date) ?? [], capacityByDate, weeklyCapacityByWeekEnd));
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
        var workItems = ToWorkItems(body);
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

    private static ReservationDraft ToReservationDraft(OrderShape body, DateOnly deliveryDate) => new(
        body.CaseName ?? "",
        body.ImpressionDate,
        body.ProductCategory,
        body.Material,
        ToWorkItems(body),
        deliveryDate,
        body.Shade,
        body.Notes,
        body.ColorNote);

    private static OrderWorkItem[] ToWorkItems(OrderShape body) =>
        body.WorkItems?
            .Select(i => new OrderWorkItem(i.ConstructionType, new ToothRange(i.ToothStart, i.ToothEnd)))
            .ToArray() ?? [];

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
        var daily = CapacityLoadRank(status.ExistingDailyCapacityUsed, status.DailyCapacityLimit);
        var weekly = CapacityLoadRank(status.ExistingWeeklyCapacityUsed, status.WeeklyCapacityLimit);
        var rank = Math.Max(daily ?? -1, weekly ?? -1);
        return rank switch
        {
            0 => "low",
            1 => "medium",
            2 => "high",
            _ => null
        };
    }

    private static int? CapacityLoadRank(decimal? used, decimal? limit)
    {
        if (!used.HasValue || !limit.HasValue || limit.Value <= 0m)
            return null;
        var ratio = used.Value / limit.Value;
        return ratio < 0.4m ? 0 : ratio < 0.8m ? 1 : 2;
    }

    private static object ToCalendarDayDto(
        DateOnly date,
        IEnumerable<OrderRecord> orders,
        IEnumerable<ReservationRecord> deliveryReservations,
        IEnumerable<ReservationRecord> impressionReservations,
        IReadOnlyDictionary<DateOnly, DailyCapacityUsage>? capacityByDate,
        IReadOnlyDictionary<DateOnly, WeeklyCapacityUsage>? weeklyCapacityByWeekEnd)
    {
        var orderDtos = orders.Select(o => ToDto(o));
        var deliveryReservationDtos = deliveryReservations.Select(r => ToDto(r));
        var impressionReservationDtos = impressionReservations.Select(r => ToDto(r));
        DailyCapacityUsage? capacity = null;
        WeeklyCapacityUsage? weeklyCapacity = null;
        var hasDailyCapacity = capacityByDate != null && capacityByDate.TryGetValue(date, out capacity);
        var hasWeeklyCapacity = weeklyCapacityByWeekEnd != null && weeklyCapacityByWeekEnd.TryGetValue(date, out weeklyCapacity);
        if (hasDailyCapacity && hasWeeklyCapacity)
            return new { date, orders = orderDtos, reservations = deliveryReservationDtos, impressionReservations = impressionReservationDtos, capacity = ToDailyCapacityDto(capacity!), weeklyCapacity = ToWeeklyCapacityDto(weeklyCapacity!) };
        if (hasDailyCapacity)
            return new { date, orders = orderDtos, reservations = deliveryReservationDtos, impressionReservations = impressionReservationDtos, capacity = ToDailyCapacityDto(capacity!) };
        if (hasWeeklyCapacity)
            return new { date, orders = orderDtos, reservations = deliveryReservationDtos, impressionReservations = impressionReservationDtos, weeklyCapacity = ToWeeklyCapacityDto(weeklyCapacity!) };
        return new { date, orders = orderDtos, reservations = deliveryReservationDtos, impressionReservations = impressionReservationDtos };
    }

    private static object ToDailyCapacityDto(DailyCapacityUsage capacity) => new
    {
        capacity.Used,
        capacity.Limit
    };

    private static object ToWeeklyCapacityDto(WeeklyCapacityUsage capacity) => new
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

    private static Task<Dictionary<string, object>> BuildClinicsMetaMapAsync(
        ISchedulingIdentityRepository identities,
        IEnumerable<OrderRecord> orders,
        CancellationToken ct) =>
        BuildClinicsMetaMapAsync(identities, orders.Select(o => o.ClinicCode), ct);

    private static async Task<Dictionary<string, object>> BuildClinicsMetaMapAsync(
        ISchedulingIdentityRepository identities,
        IEnumerable<string> clinicCodes,
        CancellationToken ct)
    {
        var needed = clinicCodes
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
                type = "order",
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
            type = "order",
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

    private static object ToDto(ReservationRecord r, SchedulingClinic? liveClinic = null)
    {
        var baseDto = new
        {
            type = "reservation",
            r.Id,
            r.ClinicCode,
            r.ClinicDisplayName,
            clinicDisplayColor = liveClinic?.DisplayColor,
            linkedClientNickname = liveClinic?.LinkedClientNickname,
            r.MemberId,
            r.MemberLabel,
            r.CaseName,
            r.ImpressionDate,
            r.ProductCategory,
            r.Material,
            workItems = r.WorkItems.Select(ToWorkItemDto),
            r.RequestedDeliveryDate,
            r.Status,
            r.Shade,
            r.Notes,
            r.ColorNote,
            r.CalculatedCapacityUnits,
            r.CreatedAt,
            r.UpdatedAt,
            r.PromotedOrderId,
            r.PromotedOrderCode,
            r.PromotedAt
        };
        return baseDto;
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

    private static object ToLabOffdayDto(LabOffdayRecord c) => new
    {
        c.Id,
        c.StartDate,
        c.EndDate,
        c.CreatedAt,
        c.UpdatedAt
    };

    private static string LabOffdayDisplay(LabOffdayRecord c) =>
        c.StartDate == c.EndDate
            ? c.StartDate.ToString("yyyy-MM-dd")
            : $"{c.StartDate:yyyy-MM-dd} - {c.EndDate:yyyy-MM-dd}";

    private static DateOnly ToLabLocalDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(LabTimeZone.ToLabLocal(timestamp).DateTime);

    private static IReadOnlyList<DateOnly> ExpandLabOffdayDates(IEnumerable<LabOffdayRecord> rows, DateOnly start, DateOnly end)
    {
        var dates = new SortedSet<DateOnly>();
        foreach (var row in rows)
        {
            var rangeStart = row.StartDate < start ? start : row.StartDate;
            var rangeEnd = row.EndDate > end ? end : row.EndDate;
            for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
                dates.Add(d);
        }

        return dates.ToArray();
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

    public sealed record ReservationDateAvailabilityRequest : OrderShape
    {
        public DateOnly Start { get; init; }
        public DateOnly End { get; init; }
        public DateOnly RequestedDeliveryDate { get; init; }
        public long? ReservationId { get; init; }
    }

    public sealed record CreateReservationRequest : OrderShape
    {
        public DateOnly RequestedDeliveryDate { get; init; }
        public string? ClinicCode { get; init; }
    }

    public sealed record UpdateReservationRequest : OrderShape
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
