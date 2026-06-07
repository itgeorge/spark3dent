using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteAuthSessionRepo : IAuthSessionRepository
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteAuthSessionRepo(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddSessionAsync(AuthSession session, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        ctx.SchedulingAuthSessions.Add(ToEntity(session));
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<AuthSession?> FindSessionByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingAuthSessions.AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
        return entity == null ? null : ToDomain(entity);
    }

    public async Task RefreshSessionAsync(string sessionId, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingAuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (entity == null) return;
        entity.LastSeenAt = lastSeenAt;
        entity.ExpiresAt = expiresAt;
        await ctx.SaveChangesAsync(ct);
    }

    public async Task RevokeSessionAsync(string sessionId, DateTimeOffset revokedAt, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingAuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (entity == null) return;
        entity.RevokedAt = revokedAt;
        await ctx.SaveChangesAsync(ct);
    }

    public async Task RevokeOrganizationSessionsAsync(OrganizationType organizationType, string organizationCode, DateTimeOffset revokedAt, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        await ctx.SchedulingAuthSessions
            .Where(s => s.OrganizationType == organizationType && s.OrganizationCode == organizationCode && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, revokedAt), ct);
    }

    public async Task RevokeMemberSessionsAsync(OrganizationType organizationType, string organizationCode, string memberId, DateTimeOffset revokedAt, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        await ctx.SchedulingAuthSessions
            .Where(s => s.OrganizationType == organizationType && s.OrganizationCode == organizationCode && s.MemberId == memberId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, revokedAt), ct);
    }

    private static SchedulingAuthSessionEntity ToEntity(AuthSession session) => new()
    {
        Id = session.Id,
        OrganizationType = session.OrganizationType,
        OrganizationCode = session.OrganizationCode,
        MemberId = session.MemberId,
        TokenHash = session.TokenHash,
        CreatedAt = session.CreatedAt,
        LastSeenAt = session.LastSeenAt,
        ExpiresAt = session.ExpiresAt,
        AbsoluteExpiresAt = session.AbsoluteExpiresAt,
        RevokedAt = session.RevokedAt,
        CreatedIp = session.CreatedIp,
        CreatedUserAgent = session.CreatedUserAgent
    };

    private static AuthSession ToDomain(SchedulingAuthSessionEntity e) => new(
        e.Id,
        e.OrganizationType,
        e.OrganizationCode,
        e.MemberId,
        e.TokenHash,
        e.CreatedAt,
        e.LastSeenAt,
        e.ExpiresAt,
        e.AbsoluteExpiresAt,
        e.RevokedAt,
        e.CreatedIp,
        e.CreatedUserAgent);
}
