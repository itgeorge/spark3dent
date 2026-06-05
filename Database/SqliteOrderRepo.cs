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

    private static IQueryable<SchedulingOrderEntity> OrderedLimited(IQueryable<SchedulingOrderEntity> query, int limit) =>
        query
            .OrderByDescending(o => o.RequestedDeliveryDate)
            .ThenByDescending(o => o.CreatedAtUnixTimeMilliseconds)
            .ThenByDescending(o => o.Id)
            .Take(Math.Clamp(limit, 1, 500));

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
        var workItems = ResolveWorkItemsForSave(order);
        var primary = workItems[0];
        entity.ProductCategory = order.ProductCategory.ToString();
        entity.WorkType = order.WorkType.ToString();
        entity.Material = order.Material.ToString();
        entity.ConstructionType = primary.ConstructionType.ToString();
        entity.ToothStart = primary.ToothStart;
        entity.ToothEnd = primary.ToothEnd;
        entity.AbutmentTeeth = OrderWorkItem.AbutmentsCsv(workItems);
        entity.WorkItemsJson = SerializeWorkItems(workItems);
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
        var constructionType = Enum.Parse<ConstructionType>(e.ConstructionType);
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
            Enum.Parse<WorkType>(e.WorkType),
            Enum.Parse<Material>(e.Material),
            constructionType,
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
            e.CreatedUserAgent,
            DeserializeWorkItems(e.WorkItemsJson, constructionType, e.ToothStart, e.ToothEnd));
    }

    private static IReadOnlyList<OrderWorkItem> ResolveWorkItemsForSave(OrderRecord order)
    {
        var legacy = new OrderWorkItem(order.ConstructionType, new ToothRange(order.ToothStart, order.ToothEnd));
        if (order.WorkItems.Count == 0)
            return [legacy];
        if (order.WorkItems.Count == 1)
        {
            var only = order.WorkItems[0];
            if (only.ConstructionType != legacy.ConstructionType || only.ToothStart != legacy.ToothStart || only.ToothEnd != legacy.ToothEnd)
                return [legacy];
        }
        return order.WorkItems;
    }

    private static string SerializeWorkItems(IReadOnlyList<OrderWorkItem> items) =>
        JsonSerializer.Serialize(items.Select(i => new WorkItemJson(i.ConstructionType, i.ToothStart, i.ToothEnd)), WorkItemsJsonOptions);

    private static IReadOnlyList<OrderWorkItem> DeserializeWorkItems(string? json, ConstructionType legacyConstructionType, int legacyToothStart, int legacyToothEnd)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [new OrderWorkItem(legacyConstructionType, new ToothRange(legacyToothStart, legacyToothEnd))];

        var items = JsonSerializer.Deserialize<List<WorkItemJson>>(json, WorkItemsJsonOptions) ?? [];
        return items.Count == 0
            ? [new OrderWorkItem(legacyConstructionType, new ToothRange(legacyToothStart, legacyToothEnd))]
            : items.Select(i => new OrderWorkItem(i.ConstructionType, new ToothRange(i.ToothStart, i.ToothEnd))).ToArray();
    }

    private sealed record WorkItemJson(ConstructionType ConstructionType, int ToothStart, int ToothEnd);
}
