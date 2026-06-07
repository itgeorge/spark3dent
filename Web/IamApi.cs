using System.Text.Json;
using Orders;

namespace Web;

public static class IamApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void MapRoutes(WebApplication app)
    {
        var iam = app.MapGroup("/api/iam")
            .AddEndpointFilter(SchedulingEndpointAuth.RequireLabActorAsync);

        iam.MapGet("/lab", async (ISchedulingIdentityRepository identities, CancellationToken ct) =>
        {
            var lab = await identities.GetLabAsync(includeInactive: true, ct)
                ?? throw new InvalidOperationException("Lab not found.");
            var members = await identities.ListMembersAsync(OrganizationType.Lab, lab.Code, includeInactive: true, ct);
            return Results.Json(new
            {
                organizationType = "lab",
                code = lab.Code,
                displayName = lab.DisplayName,
                isActive = lab.IsActive,
                createdAt = lab.CreatedAt,
                updatedAt = lab.UpdatedAt,
                members = members.Select(ToMemberDto)
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

        iam.MapGet("/organizations/{code}", async (string code, ISchedulingIdentityRepository identities, CancellationToken ct) =>
        {
            if (await identities.GetLabAsync(includeInactive: true, ct) is { } lab && string.Equals(lab.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                var labMembers = await identities.ListMembersAsync(OrganizationType.Lab, lab.Code, includeInactive: true, ct);
                return Results.Json(new
                {
                    organizationType = "lab",
                    code = lab.Code,
                    displayName = lab.DisplayName,
                    linkedClientNickname = (string?)null,
                    displayColor = (string?)null,
                    isActive = lab.IsActive,
                    createdAt = lab.CreatedAt,
                    updatedAt = lab.UpdatedAt,
                    members = labMembers.Select(ToMemberDto)
                }, JsonOptions);
            }

            var clinic = await identities.GetClinicAsync(code, includeInactive: true, ct);
            if (clinic == null)
                return Results.Json(new { error = "Organization not found." }, statusCode: 404, options: JsonOptions);

            var members = await identities.ListMembersAsync(OrganizationType.Clinic, clinic.Code, includeInactive: true, ct);
            return Results.Json(new
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
            }, JsonOptions);
        });
    }

    private static object ToMemberDto(SchedulingMember member) => new
    {
        id = member.Id,
        label = member.Label,
        isActive = member.IsActive,
        createdAt = member.CreatedAt,
        updatedAt = member.UpdatedAt,
        pinFingerprint = member.PinFingerprint
    };
}
