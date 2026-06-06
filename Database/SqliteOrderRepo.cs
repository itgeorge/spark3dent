using System.Text.Json;
using System.Text.Json.Serialization;
using Database.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteOrderRepo : IOrderRepository
{
    private static readonly JsonSerializerOptions WorkItemsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

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

    public async Task<OrderRecord> UpdateOrderAsync(OrderRecord order, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingOrders.FirstOrDefaultAsync(o => o.OrderCode == order.OrderCode, ct)
            ?? throw new InvalidOperationException("Order not found.");
        ApplyToEntity(entity, order);
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
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

    public async Task<OrderPage> ListOrdersPageAsync(string? clinicCode, int limit, OrderCursor? cursor, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var query = ScopedOrders(ctx, clinicCode);
        if (cursor != null)
            query = query.Where(o =>
                o.RequestedDeliveryDate < cursor.RequestedDeliveryDate
                || (o.RequestedDeliveryDate == cursor.RequestedDeliveryDate && o.CreatedAtUnixTimeMilliseconds < cursor.CreatedAtUnixTimeMilliseconds)
                || (o.RequestedDeliveryDate == cursor.RequestedDeliveryDate && o.CreatedAtUnixTimeMilliseconds == cursor.CreatedAtUnixTimeMilliseconds && o.Id < cursor.Id));

        return await MaterializePageAsync(query, limit, ct);
    }

    public async Task<OrderPage> ListOrdersPageContainingOrderAsync(string? clinicCode, OrderRecord target, int limit, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var query = ScopedOrders(ctx, clinicCode);
        var targetCreatedMs = target.CreatedAt.ToUnixTimeMilliseconds();
        var beforeCount = await query.CountAsync(o =>
            o.RequestedDeliveryDate > target.RequestedDeliveryDate
            || (o.RequestedDeliveryDate == target.RequestedDeliveryDate && o.CreatedAtUnixTimeMilliseconds > targetCreatedMs)
            || (o.RequestedDeliveryDate == target.RequestedDeliveryDate && o.CreatedAtUnixTimeMilliseconds == targetCreatedMs && o.Id > target.Id), ct);
        var pageStart = beforeCount / limit * limit;
        return await MaterializePageAsync(query, limit, ct, pageStart);
    }

    public async Task<IReadOnlyList<OrderRecord>> FindOrdersByCodeSuffixAsync(string? clinicCode, string codeSuffix, int limit = 2, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var normalized = codeSuffix.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            return [];

        var items = await Ordered(ScopedOrders(ctx, clinicCode))
            .Where(o => o.OrderCode.EndsWith(normalized))
            .Take(Math.Clamp(limit, 1, 20))
            .ToListAsync(ct);
        return items.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<OrderRecord>> ListActiveOrdersForCalendarAsync(string? clinicCode, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var query = ctx.SchedulingOrders.AsNoTracking()
            .Where(o => o.Status != nameof(OrderStatus.Cancelled)
                && o.RequestedDeliveryDate >= start
                && o.RequestedDeliveryDate <= end);
        if (!string.IsNullOrWhiteSpace(clinicCode))
            query = query.Where(o => o.ClinicCode == clinicCode);

        var items = await query
            .OrderBy(o => o.RequestedDeliveryDate)
            .ThenBy(o => o.ClinicDisplayName)
            .ThenBy(o => o.CaseName)
            .ThenBy(o => o.OrderCode)
            .ToListAsync(ct);
        return items.Select(ToDomain).ToList();
    }

    private static IQueryable<SchedulingOrderEntity> ScopedOrders(AppDbContext ctx, string? clinicCode)
    {
        var query = ctx.SchedulingOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(clinicCode))
            query = query.Where(o => o.ClinicCode == clinicCode);
        return query;
    }

    private static async Task<OrderPage> MaterializePageAsync(IQueryable<SchedulingOrderEntity> query, int limit, CancellationToken ct, int skip = 0)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        var rows = await Ordered(query).Skip(skip).Take(safeLimit + 1).ToListAsync(ct);
        var hasMore = rows.Count > safeLimit;
        var pageRows = hasMore ? rows.Take(safeLimit).ToList() : rows;
        var items = pageRows.Select(ToDomain).ToList();
        var nextCursor = hasMore && items.Count > 0 ? OrderCursorCodec.Encode(OrderCursor.FromOrder(items[^1])) : null;
        return new OrderPage(items, nextCursor, hasMore);
    }

    private static IQueryable<SchedulingOrderEntity> OrderedLimited(IQueryable<SchedulingOrderEntity> query, int limit) =>
        Ordered(query).Take(Math.Clamp(limit, 1, 500));

    private static IOrderedQueryable<SchedulingOrderEntity> Ordered(IQueryable<SchedulingOrderEntity> query) =>
        query
            .OrderByDescending(o => o.RequestedDeliveryDate)
            .ThenByDescending(o => o.CreatedAtUnixTimeMilliseconds)
            .ThenByDescending(o => o.Id);

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19 } sqliteEx &&
        sqliteEx.Message.Contains("SchedulingOrders.OrderCode", StringComparison.OrdinalIgnoreCase);

    private static SchedulingOrderEntity ToEntity(OrderRecord order)
    {
        var entity = new SchedulingOrderEntity { Id = order.Id };
        ApplyToEntity(entity, order);
        return entity;
    }

    private static void ApplyToEntity(SchedulingOrderEntity entity, OrderRecord order)
    {
        entity.OrderCode = order.OrderCode;
        entity.ClinicCode = order.ClinicCode;
        entity.ClinicDisplayName = order.ClinicDisplayName;
        entity.CredentialId = order.CredentialId;
        entity.CredentialLabel = order.CredentialLabel;
        entity.CredentialPinHashFingerprint = order.CredentialPinHashFingerprint;
        entity.CaseName = order.CaseName;
        entity.ImpressionDate = order.ImpressionDate;
        entity.ProductCategory = order.ProductCategory.ToString();
        entity.Material = order.Material.ToString();
        entity.WorkItemsJson = SerializeWorkItems(order.WorkItems);
        entity.RequestedDeliveryDate = order.RequestedDeliveryDate;
        entity.Status = order.Status.ToString();
        entity.Shade = order.Shade;
        entity.Notes = order.Notes;
        entity.CreatedAt = order.CreatedAt;
        entity.CreatedAtUnixTimeMilliseconds = order.CreatedAt.ToUnixTimeMilliseconds();
        entity.UpdatedAt = order.UpdatedAt;
        entity.CreatedIp = order.CreatedIp;
        entity.CreatedUserAgent = order.CreatedUserAgent;
    }

    private static OrderRecord ToDomain(SchedulingOrderEntity e)
    {
        return new OrderRecord(
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
            Enum.Parse<Material>(e.Material),
            DeserializeWorkItems(e.WorkItemsJson),
            e.RequestedDeliveryDate,
            Enum.Parse<OrderStatus>(e.Status),
            e.Shade,
            e.Notes,
            e.CreatedAt,
            e.UpdatedAt,
            e.CreatedIp,
            e.CreatedUserAgent);
    }

    private static string SerializeWorkItems(IReadOnlyList<OrderWorkItem> items) =>
        JsonSerializer.Serialize(items.Select(i => new WorkItemJson(i.ConstructionType, i.ToothStart, i.ToothEnd)), WorkItemsJsonOptions);

    private static IReadOnlyList<OrderWorkItem> DeserializeWorkItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Scheduling order is missing work items.");

        var items = JsonSerializer.Deserialize<List<WorkItemJson>>(json, WorkItemsJsonOptions) ?? [];
        if (items.Count == 0)
            throw new InvalidOperationException("Scheduling order is missing work items.");
        return items.Select(i => new OrderWorkItem(i.ConstructionType, new ToothRange(i.ToothStart, i.ToothEnd))).ToArray();
    }

    private sealed record WorkItemJson(ConstructionType ConstructionType, int ToothStart, int ToothEnd);
}
