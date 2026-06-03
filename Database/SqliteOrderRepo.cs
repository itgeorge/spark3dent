using Database.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteOrderRepo : IOrderRepository
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteOrderRepo(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = ToEntity(order);
        ctx.SchedulingOrders.Add(entity);
        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new DuplicateOrderCodeException(order.OrderCode, ex);
        }
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
        var items = await OrderedLimited(ctx.SchedulingOrders.AsNoTracking(), limit).ToListAsync(ct);
        return items.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var items = await OrderedLimited(
                ctx.SchedulingOrders.AsNoTracking().Where(o => o.ClinicCode == clinicCode),
                limit)
            .ToListAsync(ct);
        return items.Select(ToDomain).ToList();
    }

    private static IQueryable<SchedulingOrderEntity> OrderedLimited(IQueryable<SchedulingOrderEntity> query, int limit) =>
        query
            .OrderByDescending(o => o.CreatedAtUnixTimeMilliseconds)
            .ThenByDescending(o => o.Id)
            .Take(Math.Clamp(limit, 1, 500));

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19 } sqliteEx &&
        sqliteEx.Message.Contains("SchedulingOrders.OrderCode", StringComparison.OrdinalIgnoreCase);

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
        Shade = order.Shade,
        Notes = order.Notes,
        CreatedAt = order.CreatedAt,
        CreatedAtUnixTimeMilliseconds = order.CreatedAt.ToUnixTimeMilliseconds(),
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
        e.Shade,
        e.Notes,
        e.CreatedAt,
        e.UpdatedAt,
        e.CreatedIp,
        e.CreatedUserAgent);
}
