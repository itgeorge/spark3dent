using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteSchedulingIdentityRepo : ISchedulingIdentityRepository
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteSchedulingIdentityRepo(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<SchedulingOrganization?> FindOrganizationByCodeAsync(string organizationCode, bool includeInactive = false, CancellationToken ct = default)
    {
        var normalized = organizationCode.Trim();
        var lab = await GetLabAsync(includeInactive, ct);
        if (lab != null && string.Equals(lab.Code, normalized, StringComparison.OrdinalIgnoreCase))
            return ToOrganization(lab);

        var clinic = await GetClinicAsync(normalized, includeInactive, ct);
        return clinic == null ? null : ToOrganization(clinic);
    }

    public async Task<SchedulingOrganization?> GetOrganizationAsync(OrganizationType organizationType, string organizationCode, bool includeInactive = false, CancellationToken ct = default)
    {
        return organizationType switch
        {
            OrganizationType.Lab => (await GetLabAsync(includeInactive, ct)) is { } lab && string.Equals(lab.Code, organizationCode, StringComparison.OrdinalIgnoreCase)
                ? ToOrganization(lab)
                : null,
            OrganizationType.Clinic => (await GetClinicAsync(organizationCode, includeInactive, ct)) is { } clinic
                ? ToOrganization(clinic)
                : null,
            _ => null
        };
    }

    public async Task<SchedulingLab?> GetLabAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var query = ctx.SchedulingLabs.AsNoTracking().AsQueryable();
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        var entity = await query.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return entity == null ? null : ToDomain(entity);
    }

    public async Task<SchedulingClinic?> GetClinicAsync(string clinicCode, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var query = ctx.SchedulingClinics.AsNoTracking().Where(x => x.Code == clinicCode.Trim());
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        var entity = await query.FirstOrDefaultAsync(ct);
        return entity == null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<SchedulingClinic>> ListClinicsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var query = ctx.SchedulingClinics.AsNoTracking().AsQueryable();
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        var entities = await query
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToList();
    }

    public async Task<SchedulingMember?> GetMemberAsync(OrganizationType organizationType, string organizationCode, string memberId, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var query = ctx.SchedulingMembers.AsNoTracking()
            .Where(x => x.OrganizationType == organizationType && x.OrganizationCode == organizationCode && x.Id == memberId);
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        var entity = await query.FirstOrDefaultAsync(ct);
        return entity == null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<SchedulingMember>> ListMembersAsync(OrganizationType organizationType, string organizationCode, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var query = ctx.SchedulingMembers.AsNoTracking()
            .Where(x => x.OrganizationType == organizationType && x.OrganizationCode == organizationCode);
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        var entities = await query
            .OrderBy(x => x.Label)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToList();
    }

    private static SchedulingOrganization ToOrganization(SchedulingLab lab) => new(OrganizationType.Lab, lab.Code, lab.DisplayName, lab.IsActive);
    private static SchedulingOrganization ToOrganization(SchedulingClinic clinic) => new(OrganizationType.Clinic, clinic.Code, clinic.DisplayName, clinic.IsActive);

    private static SchedulingLab ToDomain(SchedulingLabEntity entity) => new(
        entity.Id,
        entity.Code,
        entity.DisplayName,
        entity.IsActive,
        entity.CreatedAt,
        entity.UpdatedAt);

    private static SchedulingClinic ToDomain(SchedulingClinicEntity entity) => new(
        entity.Code,
        entity.DisplayName,
        entity.LinkedClientNickname,
        entity.DisplayColor,
        entity.IsActive,
        entity.CreatedAt,
        entity.UpdatedAt);

    private static SchedulingMember ToDomain(SchedulingMemberEntity entity) => new(
        entity.OrganizationType,
        entity.OrganizationCode,
        entity.Id,
        entity.Label,
        entity.PinHash,
        entity.IsActive,
        entity.CreatedAt,
        entity.UpdatedAt);
}
