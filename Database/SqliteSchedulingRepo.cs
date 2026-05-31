using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteSchedulingRepo : ISchedulingRepository
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteSchedulingRepo(Func<AppDbContext> contextFactory)
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

    public async Task RevokeClinicSessionsAsync(string clinicCode, DateTimeOffset revokedAt, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        await ctx.SchedulingAuthSessions
            .Where(s => s.ClinicCode == clinicCode && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, revokedAt), ct);
    }

    public async Task RevokeCredentialSessionsAsync(string clinicCode, string credentialId, DateTimeOffset revokedAt, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        await ctx.SchedulingAuthSessions
            .Where(s => s.ClinicCode == clinicCode && s.CredentialId == credentialId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, revokedAt), ct);
    }

    public async Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        return await ctx.SchedulingOrders.AnyAsync(o => o.OrderCode == orderCode, ct);
    }

    public async Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = ToEntity(order);
        ctx.SchedulingOrders.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderCode == orderCode, ct);
        return entity == null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var allItems = await ctx.SchedulingOrders.AsNoTracking().ToListAsync(ct);
        var items = allItems
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToList();
        return items.Select(ToDomain).ToList();
    }

    private static SchedulingAuthSessionEntity ToEntity(AuthSession session) => new()
    {
        Id = session.Id,
        ClinicCode = session.ClinicCode,
        CredentialId = session.CredentialId,
        TokenHash = session.TokenHash,
        CreatedAt = session.CreatedAt,
        LastSeenAt = session.LastSeenAt,
        ExpiresAt = session.ExpiresAt,
        AbsoluteExpiresAt = session.AbsoluteExpiresAt,
        RevokedAt = session.RevokedAt,
        CreatedIp = session.CreatedIp,
        CreatedUserAgent = session.CreatedUserAgent
    };

    private static AuthSession ToDomain(SchedulingAuthSessionEntity e) => new(e.Id, e.ClinicCode, e.CredentialId, e.TokenHash, e.CreatedAt, e.LastSeenAt, e.ExpiresAt, e.AbsoluteExpiresAt, e.RevokedAt, e.CreatedIp, e.CreatedUserAgent);

    private static SchedulingOrderEntity ToEntity(OrderRecord order) => new()
    {
        Id = order.Id,
        OrderCode = order.OrderCode,
        ClinicCode = order.ClinicCode,
        ClinicDisplayName = order.ClinicDisplayName,
        CredentialId = order.CredentialId,
        CredentialLabel = order.CredentialLabel,
        CredentialPinHashFingerprint = order.CredentialPinHashFingerprint,
        CaseName = order.CaseName,
        ImpressionDate = order.ImpressionDate,
        ProductCategory = order.ProductCategory.ToString(),
        WorkType = order.WorkType.ToString(),
        Material = order.Material.ToString(),
        ConstructionType = order.ConstructionType.ToString(),
        ToothStart = order.ToothStart,
        ToothEnd = order.ToothEnd,
        AbutmentTeeth = order.AbutmentTeeth,
        RequestedDeliveryDate = order.RequestedDeliveryDate,
        Status = order.Status.ToString(),
        Notes = order.Notes,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt,
        CreatedIp = order.CreatedIp,
        CreatedUserAgent = order.CreatedUserAgent
    };

    private static OrderRecord ToDomain(SchedulingOrderEntity e) => new(
        e.Id,
        e.OrderCode,
        e.ClinicCode,
        e.ClinicDisplayName,
        e.CredentialId,
        e.CredentialLabel,
        e.CredentialPinHashFingerprint,
        e.CaseName,
        e.ImpressionDate,
        Enum.Parse<ProductCategory>(e.ProductCategory),
        Enum.Parse<WorkType>(e.WorkType),
        Enum.Parse<Material>(e.Material),
        Enum.Parse<ConstructionType>(e.ConstructionType),
        e.ToothStart,
        e.ToothEnd,
        e.AbutmentTeeth,
        e.RequestedDeliveryDate,
        Enum.Parse<OrderStatus>(e.Status),
        e.Notes,
        e.CreatedAt,
        e.UpdatedAt,
        e.CreatedIp,
        e.CreatedUserAgent);
}
