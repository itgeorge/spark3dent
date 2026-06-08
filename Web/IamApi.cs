using System.Text.Json;
using System.Text.RegularExpressions;
using Accounting;
using Orders;
using Utilities;

namespace Web;

public static class IamApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex CodeRegex = new("^[A-Z0-9][A-Z0-9_-]{0,31}$", RegexOptions.Compiled);
    private static readonly Regex MemberIdRegex = new("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex ColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ReservedOrganizationCodes = new(StringComparer.OrdinalIgnoreCase) { "LAB", "IAM", "INVOICING", "SCHEDULER" };

    public static void MapRoutes(WebApplication app)
    {
        var iam = app.MapGroup("/api/iam")
            .AddEndpointFilter(SchedulingEndpointAuth.RequireLabActorAsync);

        iam.MapGet("/lab", async (ISchedulingIdentityRepository identities, CancellationToken ct) =>
        {
            var lab = await identities.GetLabAsync(includeInactive: true, ct)
                ?? throw new InvalidOperationException("Lab not found.");
            var members = await identities.ListMembersAsync(OrganizationType.Lab, lab.Code, includeInactive: true, ct);
            return Results.Json(ToOrganizationDetailDto(lab, members), JsonOptions);
        });

        iam.MapGet("/clients", async (IClientRepo clients, string? query, int? limit) =>
        {
            var take = Math.Clamp(limit ?? 20, 1, 50);
            var result = await clients.ListAsync(200);
            var q = query?.Trim();
            var filtered = string.IsNullOrWhiteSpace(q)
                ? result.Items
                : result.Items.Where(c =>
                    c.Nickname.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || c.Address.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || c.Address.CompanyIdentifier.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || c.Address.City.Contains(q, StringComparison.OrdinalIgnoreCase));
            var items = filtered.Take(take).Select(ToClientSearchDto).ToList();
            return Results.Json(new { items }, JsonOptions);
        });

        iam.MapGet("/clients/{nickname}/prefill", async (string nickname, IClientRepo clients, ISchedulingIdentityRepository identities) =>
        {
            Client client;
            try { client = await clients.GetAsync(nickname); }
            catch (InvalidOperationException) { return Results.Json(new { error = "Client not found." }, statusCode: 404, options: JsonOptions); }

            var suggested = NormalizeOrganizationCode(client.Nickname);
            if (string.IsNullOrWhiteSpace(suggested) || ReservedOrganizationCodes.Contains(suggested) || await identities.FindOrganizationByCodeAsync(suggested, includeInactive: true) != null)
                suggested = NextSuggestedCode(suggested, client.Address.Name);

            return Results.Json(new
            {
                client = ToClientSearchDto(client),
                suggestedClinicCode = suggested,
                displayName = client.Address.Name,
                linkedClientNickname = client.Nickname,
                displayColor = GenerateColor(client.Nickname + ":" + client.Address.Name)
            }, JsonOptions);
        });

        iam.MapGet("/organizations", async (ISchedulingIdentityRepository identities, bool? includeInactive, CancellationToken ct) =>
        {
            var include = includeInactive ?? false;
            var items = new List<object>();

            if (await identities.GetLabAsync(include, ct) is { } lab)
            {
                var labMembers = await identities.ListMembersAsync(OrganizationType.Lab, lab.Code, includeInactive: true, ct);
                items.Add(new
                {
                    organizationType = "lab",
                    code = lab.Code,
                    displayName = lab.DisplayName,
                    linkedClientNickname = (string?)null,
                    displayColor = (string?)null,
                    isActive = lab.IsActive,
                    memberCount = labMembers.Count,
                    activeMemberCount = labMembers.Count(m => m.IsActive)
                });
            }

            var clinics = await identities.ListClinicsAsync(include, ct);
            foreach (var clinic in clinics)
            {
                var members = await identities.ListMembersAsync(OrganizationType.Clinic, clinic.Code, includeInactive: true, ct);
                items.Add(new
                {
                    organizationType = "clinic",
                    code = clinic.Code,
                    displayName = clinic.DisplayName,
                    linkedClientNickname = clinic.LinkedClientNickname,
                    displayColor = clinic.DisplayColor,
                    isActive = clinic.IsActive,
                    memberCount = members.Count,
                    activeMemberCount = members.Count(m => m.IsActive)
                });
            }
            return Results.Json(new { items }, JsonOptions);
        });

        iam.MapPost("/organizations", async (HttpContext ctx, ISchedulingIdentityRepository identities, IClientRepo clients, PinHasher hasher, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var body = await ReadJson<OrganizationCreateDto>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            var err = await ValidateOrganizationCreateAsync(body, identities, clients, ct);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400, options: JsonOptions);

            var now = clock.UtcNow;
            var code = NormalizeOrganizationCode(body.Code!);
            var clinic = await identities.CreateClinicWithInitialMemberAsync(
                new ClinicCreateRequest(code, body.DisplayName!.Trim(), EmptyToNull(body.LinkedClientNickname), EmptyToNull(body.DisplayColor), now),
                new MemberCreateRequest(body.InitialMember!.Id!.Trim(), body.InitialMember.Label!.Trim(), hasher.Hash(body.InitialMember.Secret!), now),
                ct);
            var members = await identities.ListMembersAsync(OrganizationType.Clinic, clinic.Code, includeInactive: true, ct);
            await AppendAuditAsync(auditLog, clock, ctx, "ClinicCreated", "Clinic", clinic.Code, clinic.DisplayName,
                new { clinic.Code, clinic.LinkedClientNickname, clinic.DisplayColor, initialMemberId = body.InitialMember.Id!.Trim() });
            await AppendAuditAsync(auditLog, clock, ctx, "MemberCreated", "Member", $"Clinic:{clinic.Code}:{body.InitialMember.Id!.Trim()}", body.InitialMember.Label!.Trim(),
                new { organizationType = "Clinic", organizationCode = clinic.Code, memberId = body.InitialMember.Id!.Trim(), source = "clinic-create" });
            return Results.Json(new { organization = ToOrganizationDetailDto(clinic, members) }, statusCode: 201, options: JsonOptions);
        });

        iam.MapGet("/organizations/{code}", async (string code, ISchedulingIdentityRepository identities, CancellationToken ct) =>
        {
            var detail = await LoadOrganizationDetailAsync(identities, code, ct);
            return detail == null
                ? Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions)
                : Results.Json(detail, JsonOptions);
        });

        iam.MapPut("/organizations/{code}", async (string code, HttpContext ctx, ISchedulingIdentityRepository identities, IClientRepo clients, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var lab = await identities.GetLabAsync(includeInactive: true, ct);
            if (lab != null && string.Equals(lab.Code, code, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "Lab profile editing is not supported by this endpoint." }, statusCode: 400, options: JsonOptions);
            var existing = await identities.GetClinicAsync(code, includeInactive: true, ct);
            if (existing == null) return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);
            var body = await ReadJson<OrganizationUpdateDto>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            var err = await ValidateOrganizationUpdateAsync(body, clients, ct);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400, options: JsonOptions);

            var updated = await identities.UpdateClinicAsync(existing.Code, new ClinicUpdateRequest(body.DisplayName!.Trim(), EmptyToNull(body.LinkedClientNickname), EmptyToNull(body.DisplayColor), clock.UtcNow), ct);
            var members = await identities.ListMembersAsync(OrganizationType.Clinic, updated.Code, includeInactive: true, ct);
            await AppendAuditAsync(auditLog, clock, ctx, "ClinicUpdated", "Clinic", updated.Code, updated.DisplayName,
                new { old = new { existing.DisplayName, existing.LinkedClientNickname, existing.DisplayColor }, updated = new { updated.DisplayName, updated.LinkedClientNickname, updated.DisplayColor } });
            return Results.Json(ToOrganizationDetailDto(updated, members), JsonOptions);
        });

        iam.MapDelete("/organizations/{code}", async (string code, HttpContext ctx, ISchedulingIdentityRepository identities, SchedulingAuthService auth, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var clinic = await identities.GetClinicAsync(code, includeInactive: true, ct);
            if (clinic == null) return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);
            var updated = await identities.SetClinicActiveAsync(clinic.Code, false, clock.UtcNow, ct);
            await auth.RevokeOrganizationSessionsAsync(OrganizationType.Clinic, updated.Code, ct);
            var members = await identities.ListMembersAsync(OrganizationType.Clinic, updated.Code, includeInactive: true, ct);
            await AppendAuditAsync(auditLog, clock, ctx, "ClinicDeactivated", "Clinic", updated.Code, updated.DisplayName, new { updated.Code });
            return Results.Json(ToOrganizationDetailDto(updated, members), JsonOptions);
        });

        iam.MapPost("/organizations/{code}/reactivate", async (string code, HttpContext ctx, ISchedulingIdentityRepository identities, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var clinic = await identities.GetClinicAsync(code, includeInactive: true, ct);
            if (clinic == null) return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);
            var updated = await identities.SetClinicActiveAsync(clinic.Code, true, clock.UtcNow, ct);
            var members = await identities.ListMembersAsync(OrganizationType.Clinic, updated.Code, includeInactive: true, ct);
            await AppendAuditAsync(auditLog, clock, ctx, "ClinicReactivated", "Clinic", updated.Code, updated.DisplayName, new { updated.Code });
            return Results.Json(ToOrganizationDetailDto(updated, members), JsonOptions);
        });

        iam.MapPost("/organizations/{code}/members", async (string code, HttpContext ctx, ISchedulingIdentityRepository identities, PinHasher hasher, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var target = await ResolveOrganizationAsync(identities, code, ct);
            if (target == null) return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);
            var body = await ReadJson<MemberCreateDto>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            var err = ValidateMemberCreate(body);
            if (err != null) return Results.Json(new { error = err }, statusCode: 400, options: JsonOptions);
            var now = clock.UtcNow;
            var member = await identities.CreateMemberAsync(target.Value.Type, target.Value.Code, new MemberCreateRequest(body.Id!.Trim(), body.Label!.Trim(), hasher.Hash(body.Secret!), now), ct);
            await AppendAuditAsync(auditLog, clock, ctx, "MemberCreated", "Member", MemberEntityId(member), member.Label, new { organizationType = member.OrganizationType.ToString(), member.OrganizationCode, memberId = member.Id });
            return Results.Json(new { member = ToMemberDto(member), organization = await LoadOrganizationDetailAsync(identities, target.Value.Code, ct) }, statusCode: 201, options: JsonOptions);
        });

        iam.MapPut("/organizations/{code}/members/{memberId}", async (string code, string memberId, HttpContext ctx, ISchedulingIdentityRepository identities, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var target = await ResolveOrganizationAsync(identities, code, ct);
            if (target == null) return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);
            var body = await ReadJson<MemberUpdateDto>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            if (string.IsNullOrWhiteSpace(body.Label) || body.Label.Trim().Length > 120) return Results.Json(new { error = "label is required." }, statusCode: 400, options: JsonOptions);
            var member = await identities.UpdateMemberLabelAsync(target.Value.Type, target.Value.Code, memberId, body.Label.Trim(), clock.UtcNow, ct);
            await AppendAuditAsync(auditLog, clock, ctx, "MemberUpdated", "Member", MemberEntityId(member), member.Label, new { organizationType = member.OrganizationType.ToString(), member.OrganizationCode, memberId = member.Id });
            return Results.Json(new { member = ToMemberDto(member), organization = await LoadOrganizationDetailAsync(identities, target.Value.Code, ct) }, JsonOptions);
        });

        iam.MapDelete("/organizations/{code}/members/{memberId}", async (string code, string memberId, HttpContext ctx, ISchedulingIdentityRepository identities, SchedulingAuthService auth, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var target = await ResolveOrganizationAsync(identities, code, ct);
            if (target == null) return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);
            var member = await identities.SetMemberActiveAsync(target.Value.Type, target.Value.Code, memberId, false, clock.UtcNow, ct);
            await auth.RevokeMemberSessionsAsync(member.OrganizationType, member.OrganizationCode, member.Id, ct);
            await AppendAuditAsync(auditLog, clock, ctx, "MemberDeactivated", "Member", MemberEntityId(member), member.Label, new { organizationType = member.OrganizationType.ToString(), member.OrganizationCode, memberId = member.Id });
            return Results.Json(new { member = ToMemberDto(member), organization = await LoadOrganizationDetailAsync(identities, target.Value.Code, ct) }, JsonOptions);
        });

        iam.MapPost("/organizations/{code}/members/{memberId}/reactivate", async (string code, string memberId, HttpContext ctx, ISchedulingIdentityRepository identities, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var target = await ResolveOrganizationAsync(identities, code, ct);
            if (target == null) return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);
            var member = await identities.SetMemberActiveAsync(target.Value.Type, target.Value.Code, memberId, true, clock.UtcNow, ct);
            await AppendAuditAsync(auditLog, clock, ctx, "MemberReactivated", "Member", MemberEntityId(member), member.Label, new { organizationType = member.OrganizationType.ToString(), member.OrganizationCode, memberId = member.Id });
            return Results.Json(new { member = ToMemberDto(member), organization = await LoadOrganizationDetailAsync(identities, target.Value.Code, ct) }, JsonOptions);
        });

        iam.MapPost("/organizations/{code}/members/{memberId}/secret", async (string code, string memberId, HttpContext ctx, ISchedulingIdentityRepository identities, SchedulingAuthService auth, PinHasher hasher, IAuditLog auditLog, IClock clock, CancellationToken ct) =>
        {
            var target = await ResolveOrganizationAsync(identities, code, ct);
            if (target == null) return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);
            var body = await ReadJson<MemberSecretDto>(ctx);
            if (body == null) return Results.Json(new { error = "Invalid JSON body." }, statusCode: 400, options: JsonOptions);
            try { PinHasher.ValidatePinShape(body.Secret ?? string.Empty); }
            catch (InvalidOperationException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400, options: JsonOptions); }
            var member = await identities.UpdateMemberSecretAsync(target.Value.Type, target.Value.Code, memberId, hasher.Hash(body.Secret!), clock.UtcNow, ct);
            await auth.RevokeMemberSessionsAsync(member.OrganizationType, member.OrganizationCode, member.Id, ct);
            await AppendAuditAsync(auditLog, clock, ctx, "MemberSecretRotated", "Member", MemberEntityId(member), member.Label, new { organizationType = member.OrganizationType.ToString(), member.OrganizationCode, memberId = member.Id });
            return Results.Json(new { member = ToMemberDto(member), organization = await LoadOrganizationDetailAsync(identities, target.Value.Code, ct) }, JsonOptions);
        });
    }

    private static async Task<object?> LoadOrganizationDetailAsync(ISchedulingIdentityRepository identities, string code, CancellationToken ct)
    {
        if (await identities.GetLabAsync(includeInactive: true, ct) is { } lab && string.Equals(lab.Code, code, StringComparison.OrdinalIgnoreCase))
        {
            var labMembers = await identities.ListMembersAsync(OrganizationType.Lab, lab.Code, includeInactive: true, ct);
            return ToOrganizationDetailDto(lab, labMembers);
        }

        var clinic = await identities.GetClinicAsync(code, includeInactive: true, ct);
        if (clinic == null) return null;
        var members = await identities.ListMembersAsync(OrganizationType.Clinic, clinic.Code, includeInactive: true, ct);
        return ToOrganizationDetailDto(clinic, members);
    }

    private static async Task<(OrganizationType Type, string Code)?> ResolveOrganizationAsync(ISchedulingIdentityRepository identities, string code, CancellationToken ct)
    {
        if (await identities.GetLabAsync(includeInactive: true, ct) is { } lab && string.Equals(lab.Code, code, StringComparison.OrdinalIgnoreCase))
            return (OrganizationType.Lab, lab.Code);
        if (await identities.GetClinicAsync(code, includeInactive: true, ct) is { } clinic)
            return (OrganizationType.Clinic, clinic.Code);
        return null;
    }

    private static object ToOrganizationDetailDto(SchedulingLab lab, IReadOnlyList<SchedulingMember> members) => new
    {
        organizationType = "lab",
        code = lab.Code,
        displayName = lab.DisplayName,
        linkedClientNickname = (string?)null,
        displayColor = (string?)null,
        isActive = lab.IsActive,
        createdAt = lab.CreatedAt,
        updatedAt = lab.UpdatedAt,
        members = members.Select(ToMemberDto)
    };

    private static object ToOrganizationDetailDto(SchedulingClinic clinic, IReadOnlyList<SchedulingMember> members) => new
    {
        organizationType = "clinic",
        code = clinic.Code,
        displayName = clinic.DisplayName,
        linkedClientNickname = clinic.LinkedClientNickname,
        displayColor = clinic.DisplayColor,
        isActive = clinic.IsActive,
        createdAt = clinic.CreatedAt,
        updatedAt = clinic.UpdatedAt,
        members = members.Select(ToMemberDto)
    };

    private static object ToMemberDto(SchedulingMember member) => new
    {
        id = member.Id,
        label = member.Label,
        isActive = member.IsActive,
        createdAt = member.CreatedAt,
        updatedAt = member.UpdatedAt,
        pinFingerprint = member.PinFingerprint
    };

    private static object ToClientSearchDto(Client c) => new
    {
        nickname = c.Nickname,
        name = c.Address.Name,
        companyIdentifier = c.Address.CompanyIdentifier,
        city = c.Address.City
    };

    private static async Task<string?> ValidateOrganizationCreateAsync(OrganizationCreateDto body, ISchedulingIdentityRepository identities, IClientRepo clients, CancellationToken ct)
    {
        var code = NormalizeOrganizationCode(body.Code ?? string.Empty);
        if (!CodeRegex.IsMatch(code)) return "code must be 1-32 characters: uppercase letters, numbers, '-' or '_'.";
        if (ReservedOrganizationCodes.Contains(code)) return "code is reserved.";
        var lab = await identities.GetLabAsync(includeInactive: true, ct);
        if (lab != null && string.Equals(lab.Code, code, StringComparison.OrdinalIgnoreCase)) return "code is reserved.";
        if (await identities.FindOrganizationByCodeAsync(code, includeInactive: true, ct) != null) return "Organization code already exists.";
        if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Trim().Length > 120) return "displayName is required.";
        if (!string.IsNullOrWhiteSpace(body.DisplayColor) && !ColorRegex.IsMatch(body.DisplayColor.Trim())) return "displayColor must be #RRGGBB.";
        if (!await ClientExistsIfProvidedAsync(clients, body.LinkedClientNickname)) return "linked client was not found.";
        if (body.InitialMember == null) return "initialMember is required.";
        return ValidateMemberCreate(body.InitialMember);
    }

    private static async Task<string?> ValidateOrganizationUpdateAsync(OrganizationUpdateDto body, IClientRepo clients, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Trim().Length > 120) return "displayName is required.";
        if (!string.IsNullOrWhiteSpace(body.DisplayColor) && !ColorRegex.IsMatch(body.DisplayColor.Trim())) return "displayColor must be #RRGGBB.";
        if (!await ClientExistsIfProvidedAsync(clients, body.LinkedClientNickname)) return "linked client was not found.";
        return null;
    }

    private static string? ValidateMemberCreate(MemberCreateDto body)
    {
        if (string.IsNullOrWhiteSpace(body.Id) || !MemberIdRegex.IsMatch(body.Id.Trim())) return "member id must be 1-64 letters, numbers, '-' or '_'.";
        if (string.IsNullOrWhiteSpace(body.Label) || body.Label.Trim().Length > 120) return "member label is required.";
        try { PinHasher.ValidatePinShape(body.Secret ?? string.Empty); }
        catch (InvalidOperationException ex) { return ex.Message; }
        return null;
    }

    private static async Task<bool> ClientExistsIfProvidedAsync(IClientRepo clients, string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname)) return true;
        try { _ = await clients.GetAsync(nickname.Trim()); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static Task AppendAuditAsync(IAuditLog auditLog, IClock clock, HttpContext ctx, string operation, string entityType, string entityId, string? entityDisplay, object metadata)
    {
        var actor = SchedulingEndpointAuth.CurrentActor(ctx);
        var auditEvent = new AuditEvent(
            0,
            "IAM",
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
            ctx.Connection.RemoteIpAddress?.ToString(),
            string.IsNullOrWhiteSpace(ctx.Request.Headers.UserAgent.ToString()) ? null : ctx.Request.Headers.UserAgent.ToString(),
            JsonSerializer.Serialize(metadata, JsonOptions));
        return auditLog.AppendAsync(auditEvent, ctx.RequestAborted);
    }

    private static async Task<T?> ReadJson<T>(HttpContext ctx)
    {
        try { return await ctx.Request.ReadFromJsonAsync<T>(JsonOptions); }
        catch { return default; }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string MemberEntityId(SchedulingMember member) => $"{member.OrganizationType}:{member.OrganizationCode}:{member.Id}";

    private static string NormalizeOrganizationCode(string value)
    {
        var chars = value.Trim().ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : char.IsWhiteSpace(ch) ? '-' : '\0')
            .Where(ch => ch != '\0')
            .ToArray();
        var code = new string(chars);
        return code.Length > 32 ? code[..32] : code;
    }

    private static string NextSuggestedCode(string candidate, string fallback)
    {
        var baseCode = NormalizeOrganizationCode(string.IsNullOrWhiteSpace(candidate) ? fallback : candidate);
        if (string.IsNullOrWhiteSpace(baseCode) || ReservedOrganizationCodes.Contains(baseCode)) baseCode = "CLINIC";
        return baseCode.Length <= 28 ? baseCode + "-1" : baseCode[..28] + "-1";
    }

    private static string GenerateColor(string seed)
    {
        var hash = 0;
        foreach (var ch in seed) hash = unchecked(hash * 31 + ch);
        var hue = Math.Abs(hash) % 360;
        return HslToRgbHex(hue, 65, 55);
    }

    private static string HslToRgbHex(int h, int sPercent, int lPercent)
    {
        var s = sPercent / 100d;
        var l = lPercent / 100d;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h / 60d) % 2 - 1));
        var m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return $"#{(int)Math.Round((r + m) * 255):X2}{(int)Math.Round((g + m) * 255):X2}{(int)Math.Round((b + m) * 255):X2}";
    }

    private record OrganizationCreateDto(string? Code, string? DisplayName, string? LinkedClientNickname, string? DisplayColor, MemberCreateDto? InitialMember);
    private record OrganizationUpdateDto(string? DisplayName, string? LinkedClientNickname, string? DisplayColor);
    private record MemberCreateDto(string? Id, string? Label, string? Secret);
    private record MemberUpdateDto(string? Label);
    private record MemberSecretDto(string? Secret);
}
