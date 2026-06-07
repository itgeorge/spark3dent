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
        var normalized = clinicCode.Trim().ToUpperInvariant();
        var query = ctx.SchedulingClinics.AsNoTracking().Where(x => x.Code.ToUpper() == normalized);
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
        var orgCode = organizationCode.Trim().ToUpperInvariant();
        var id = memberId.Trim().ToUpperInvariant();
        var query = ctx.SchedulingMembers.AsNoTracking()
            .Where(x => x.OrganizationType == organizationType && x.OrganizationCode.ToUpper() == orgCode && x.Id.ToUpper() == id);
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        var entity = await query.FirstOrDefaultAsync(ct);
        return entity == null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<SchedulingMember>> ListMembersAsync(OrganizationType organizationType, string organizationCode, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var orgCode = organizationCode.Trim().ToUpperInvariant();
        var query = ctx.SchedulingMembers.AsNoTracking()
            .Where(x => x.OrganizationType == organizationType && x.OrganizationCode.ToUpper() == orgCode);
        if (!includeInactive)
            query = query.Where(x => x.IsActive);
        var entities = await query
            .OrderBy(x => x.Label)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToList();
    }

    public async Task<SchedulingLab> BootstrapLabAsync(LabBootstrapRequest request, bool reset, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        var existing = await ctx.SchedulingLabs.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (existing != null && !reset)
            throw new InvalidOperationException("A lab already exists. Re-run with --reset to replace bootstrap identity.");

        var oldLabCode = existing?.Code;
        if (existing == null)
        {
            existing = new SchedulingLabEntity
            {
                Id = 1,
                CreatedAt = request.Now
            };
            ctx.SchedulingLabs.Add(existing);
        }

        existing.Code = request.LabCode;
        existing.DisplayName = request.LabDisplayName;
        existing.IsActive = true;
        existing.UpdatedAt = request.Now;
        if (existing.CreatedAt == default)
            existing.CreatedAt = request.Now;

        if (!string.IsNullOrWhiteSpace(oldLabCode) && !string.Equals(oldLabCode, request.LabCode, StringComparison.OrdinalIgnoreCase))
        {
            await ctx.SchedulingMembers
                .Where(m => m.OrganizationType == OrganizationType.Lab && m.OrganizationCode == oldLabCode)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.OrganizationCode, request.LabCode), ct);
            await ctx.SchedulingAuthSessions
                .Where(s => s.OrganizationType == OrganizationType.Lab && s.OrganizationCode == oldLabCode)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.OrganizationCode, request.LabCode)
                    .SetProperty(s => s.RevokedAt, request.Now), ct);
        }
        else if (reset)
        {
            await ctx.SchedulingAuthSessions
                .Where(s => s.OrganizationType == OrganizationType.Lab && s.OrganizationCode == request.LabCode && s.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, request.Now), ct);
        }

        if (reset)
        {
            await ctx.SchedulingMembers
                .Where(m => m.OrganizationType == OrganizationType.Lab && m.OrganizationCode == request.LabCode)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.IsActive, false)
                    .SetProperty(m => m.UpdatedAt, request.Now), ct);
        }

        var member = await ctx.SchedulingMembers.FirstOrDefaultAsync(
            m => m.OrganizationType == OrganizationType.Lab && m.OrganizationCode == request.LabCode && m.Id == request.MemberId,
            ct);
        if (member == null)
        {
            member = new SchedulingMemberEntity
            {
                OrganizationType = OrganizationType.Lab,
                OrganizationCode = request.LabCode,
                Id = request.MemberId,
                CreatedAt = request.Now
            };
            ctx.SchedulingMembers.Add(member);
        }
        member.Label = request.MemberLabel;
        member.PinHash = request.MemberPinHash;
        member.IsActive = true;
        member.UpdatedAt = request.Now;
        if (member.CreatedAt == default)
            member.CreatedAt = request.Now;

        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDomain(existing);
    }

    public async Task<SchedulingClinic> CreateClinicWithInitialMemberAsync(ClinicCreateRequest request, MemberCreateRequest initialMember, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var normalized = request.Code.ToUpperInvariant();
        if (await ctx.SchedulingLabs.AnyAsync(l => l.Code.ToUpper() == normalized, ct)
            || await ctx.SchedulingClinics.AnyAsync(c => c.Code.ToUpper() == normalized, ct))
            throw new InvalidOperationException("Organization code already exists.");

        var clinic = new SchedulingClinicEntity
        {
            Code = request.Code,
            DisplayName = request.DisplayName,
            LinkedClientNickname = request.LinkedClientNickname,
            DisplayColor = request.DisplayColor,
            IsActive = true,
            CreatedAt = request.Now,
            UpdatedAt = request.Now
        };
        ctx.SchedulingClinics.Add(clinic);
        ctx.SchedulingMembers.Add(new SchedulingMemberEntity
        {
            OrganizationType = OrganizationType.Clinic,
            OrganizationCode = request.Code,
            Id = initialMember.Id,
            Label = initialMember.Label,
            PinHash = initialMember.PinHash,
            IsActive = true,
            CreatedAt = initialMember.Now,
            UpdatedAt = initialMember.Now
        });
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDomain(clinic);
    }

    public async Task<SchedulingClinic> UpdateClinicAsync(string clinicCode, ClinicUpdateRequest request, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var clinic = await FindClinicEntityAsync(ctx, clinicCode, ct) ?? throw new InvalidOperationException("Organization not found.");
        clinic.DisplayName = request.DisplayName;
        clinic.LinkedClientNickname = request.LinkedClientNickname;
        clinic.DisplayColor = request.DisplayColor;
        clinic.UpdatedAt = request.Now;
        await ctx.SaveChangesAsync(ct);
        return ToDomain(clinic);
    }

    public async Task<SchedulingClinic> SetClinicActiveAsync(string clinicCode, bool isActive, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var clinic = await FindClinicEntityAsync(ctx, clinicCode, ct) ?? throw new InvalidOperationException("Organization not found.");
        clinic.IsActive = isActive;
        clinic.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        return ToDomain(clinic);
    }

    public async Task<SchedulingMember> CreateMemberAsync(OrganizationType organizationType, string organizationCode, MemberCreateRequest request, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var organization = await FindOrganizationEntityAsync(ctx, organizationType, organizationCode, ct);
        if (organization == null)
            throw new InvalidOperationException("Organization not found.");
        if (!organization.Value.IsActive)
            throw new InvalidOperationException("Cannot add members to an inactive organization.");
        var canonicalOrgCode = organization.Value.Code;
        var normalizedId = request.Id.ToUpperInvariant();
        var duplicate = await ctx.SchedulingMembers.AnyAsync(m =>
            m.OrganizationType == organizationType && m.OrganizationCode == canonicalOrgCode && m.Id.ToUpper() == normalizedId,
            ct);
        if (duplicate)
            throw new InvalidOperationException("Member id already exists.");

        var entity = new SchedulingMemberEntity
        {
            OrganizationType = organizationType,
            OrganizationCode = canonicalOrgCode,
            Id = request.Id,
            Label = request.Label,
            PinHash = request.PinHash,
            IsActive = true,
            CreatedAt = request.Now,
            UpdatedAt = request.Now
        };
        ctx.SchedulingMembers.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async Task<SchedulingMember> UpdateMemberLabelAsync(OrganizationType organizationType, string organizationCode, string memberId, string label, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await FindMemberEntityAsync(ctx, organizationType, organizationCode, memberId, ct) ?? throw new InvalidOperationException("Member not found.");
        entity.Label = label;
        entity.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async Task<SchedulingMember> SetMemberActiveAsync(OrganizationType organizationType, string organizationCode, string memberId, bool isActive, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await FindMemberEntityAsync(ctx, organizationType, organizationCode, memberId, ct) ?? throw new InvalidOperationException("Member not found.");
        entity.IsActive = isActive;
        entity.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async Task<SchedulingMember> UpdateMemberSecretAsync(OrganizationType organizationType, string organizationCode, string memberId, string pinHash, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await FindMemberEntityAsync(ctx, organizationType, organizationCode, memberId, ct) ?? throw new InvalidOperationException("Member not found.");
        entity.PinHash = pinHash;
        entity.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    private static async Task<SchedulingClinicEntity?> FindClinicEntityAsync(AppDbContext ctx, string clinicCode, CancellationToken ct)
    {
        var normalized = clinicCode.Trim().ToUpperInvariant();
        return await ctx.SchedulingClinics.FirstOrDefaultAsync(c => c.Code.ToUpper() == normalized, ct);
    }

    private static async Task<(string Code, bool IsActive)?> FindOrganizationEntityAsync(AppDbContext ctx, OrganizationType organizationType, string organizationCode, CancellationToken ct)
    {
        var normalized = organizationCode.Trim().ToUpperInvariant();
        if (organizationType == OrganizationType.Lab)
        {
            var lab = await ctx.SchedulingLabs.FirstOrDefaultAsync(l => l.Code.ToUpper() == normalized, ct);
            return lab == null ? null : (lab.Code, lab.IsActive);
        }
        var clinic = await ctx.SchedulingClinics.FirstOrDefaultAsync(c => c.Code.ToUpper() == normalized, ct);
        return clinic == null ? null : (clinic.Code, clinic.IsActive);
    }

    private static async Task<SchedulingMemberEntity?> FindMemberEntityAsync(AppDbContext ctx, OrganizationType organizationType, string organizationCode, string memberId, CancellationToken ct)
    {
        var normalizedOrg = organizationCode.Trim().ToUpperInvariant();
        var normalizedMember = memberId.Trim().ToUpperInvariant();
        return await ctx.SchedulingMembers.FirstOrDefaultAsync(m =>
            m.OrganizationType == organizationType && m.OrganizationCode.ToUpper() == normalizedOrg && m.Id.ToUpper() == normalizedMember,
            ct);
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
