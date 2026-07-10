using System.Text.Json;
using System.Text.Json.Serialization;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteReservationRepo : IReservationRepository
{
    private static readonly JsonSerializerOptions WorkItemsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Func<AppDbContext>? _contextFactory;
    private readonly AppDbContext? _sharedContext;

    public SqliteReservationRepo(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    internal SqliteReservationRepo(AppDbContext sharedContext)
    {
        _sharedContext = sharedContext;
    }

    public Task<ReservationRecord> CreateReservationAsync(ReservationRecord reservation, CancellationToken ct = default) =>
        WithContextAsync(async ctx =>
        {
            var entity = ToEntity(reservation);
            ctx.SchedulingReservations.Add(entity);
            await ctx.SaveChangesAsync(ct);
            return ToDomain(entity);
        });

    public Task<ReservationRecord?> GetReservationByIdAsync(long id, CancellationToken ct = default) =>
        WithContextAsync(async ctx =>
        {
            var entity = await ctx.SchedulingReservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            return entity == null ? null : ToDomain(entity);
        });

    public Task<ReservationRecord> UpdateReservationAsync(ReservationRecord reservation, CancellationToken ct = default) =>
        WithContextAsync(async ctx =>
        {
            var entity = await ctx.SchedulingReservations.FirstOrDefaultAsync(r => r.Id == reservation.Id, ct)
                ?? throw new InvalidOperationException("Reservation not found.");
            ApplyToEntity(entity, reservation);
            await ctx.SaveChangesAsync(ct);
            return ToDomain(entity);
        });

    public Task<IReadOnlyList<ReservationRecord>> ListActiveReservationsForActorAsync(string? clinicCode, int limit, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        WithContextAsync(async ctx =>
        {
            var safeLimit = Math.Clamp(limit, 1, 500);
            var minActiveImpressionDate = MinNonExpiredImpressionDate(nowUtc);
            var query = ActiveBaseQuery(ctx, clinicCode)
                .Where(r => r.ImpressionDate >= minActiveImpressionDate);
            var rows = await query
                .OrderByDescending(r => r.RequestedDeliveryDate)
                .ThenByDescending(r => r.CreatedAtUnixTimeMilliseconds)
                .ThenByDescending(r => r.Id)
                .Take(safeLimit)
                .ToListAsync(ct);
            return (IReadOnlyList<ReservationRecord>)rows.Select(ToDomain)
                .Where(r => ReservationActiveRules.IsActiveForScheduling(r, nowUtc))
                .ToList();
        });

    public Task<IReadOnlyList<ReservationRecord>> ListActiveReservationsForCalendarAsync(string? clinicCode, DateOnly start, DateOnly end, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        WithContextAsync(async ctx =>
        {
            var minActiveImpressionDate = MinNonExpiredImpressionDate(nowUtc);
            var query = ActiveBaseQuery(ctx, clinicCode)
                .Where(r => r.ImpressionDate >= minActiveImpressionDate)
                .Where(r => (r.RequestedDeliveryDate >= start && r.RequestedDeliveryDate <= end)
                    || (r.ImpressionDate >= start && r.ImpressionDate <= end));
            var rows = await query
                .OrderBy(r => r.RequestedDeliveryDate)
                .ThenBy(r => r.Id)
                .ToListAsync(ct);
            return (IReadOnlyList<ReservationRecord>)rows.Select(ToDomain)
                .Where(r => ReservationActiveRules.IsActiveForScheduling(r, nowUtc))
                .ToList();
        });

    public Task<IReadOnlyList<ReservationRecord>> ListActiveReservationsByDeadlineRangeAsync(DateOnly start, DateOnly end, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        WithContextAsync(async ctx =>
        {
            var minActiveImpressionDate = MinNonExpiredImpressionDate(nowUtc);
            var rows = await ActiveBaseQuery(ctx, clinicCode: null)
                .Where(r => r.ImpressionDate >= minActiveImpressionDate)
                .Where(r => r.RequestedDeliveryDate >= start && r.RequestedDeliveryDate <= end)
                .OrderBy(r => r.RequestedDeliveryDate)
                .ThenBy(r => r.Id)
                .ToListAsync(ct);
            return (IReadOnlyList<ReservationRecord>)rows.Select(ToDomain)
                .Where(r => ReservationActiveRules.IsActiveForScheduling(r, nowUtc))
                .ToList();
        });

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> operation)
    {
        if (_sharedContext != null)
            return await operation(_sharedContext);

        if (_contextFactory == null)
            throw new InvalidOperationException("Reservation repository context is not configured.");

        await using var ctx = _contextFactory();
        return await operation(ctx);
    }

    private static DateOnly MinNonExpiredImpressionDate(DateTimeOffset nowUtc)
    {
        var labLocalDate = DateOnly.FromDateTime(LabTimeZone.ToLabLocal(nowUtc).DateTime.Date);
        return labLocalDate.AddDays(-1);
    }

    private static IQueryable<SchedulingReservationEntity> ActiveBaseQuery(AppDbContext ctx, string? clinicCode)
    {
        var query = ctx.SchedulingReservations.AsNoTracking()
            .Where(r => r.Status == nameof(ReservationStatus.Active));
        if (!string.IsNullOrWhiteSpace(clinicCode))
            query = query.Where(r => r.ClinicCode == clinicCode);
        return query;
    }

    private static SchedulingReservationEntity ToEntity(ReservationRecord reservation)
    {
        var entity = new SchedulingReservationEntity { Id = reservation.Id };
        ApplyToEntity(entity, reservation);
        return entity;
    }

    private static void ApplyToEntity(SchedulingReservationEntity entity, ReservationRecord reservation)
    {
        entity.ClinicCode = reservation.ClinicCode;
        entity.ClinicDisplayName = reservation.ClinicDisplayName;
        entity.MemberId = reservation.MemberId;
        entity.MemberLabel = reservation.MemberLabel;
        entity.MemberPinHashFingerprint = reservation.MemberPinHashFingerprint;
        entity.CaseName = reservation.CaseName;
        entity.ImpressionDate = reservation.ImpressionDate;
        entity.ProductCategory = reservation.ProductCategory.ToString();
        entity.Material = reservation.Material.ToString();
        entity.WorkItemsJson = SerializeWorkItems(reservation.WorkItems);
        entity.RequestedDeliveryDate = reservation.RequestedDeliveryDate;
        entity.Status = reservation.Status.ToString();
        entity.Shade = reservation.Shade;
        entity.Notes = reservation.Notes;
        entity.ColorNote = reservation.ColorNote;
        entity.CreatedAt = reservation.CreatedAt;
        entity.CreatedAtUnixTimeMilliseconds = reservation.CreatedAt.ToUnixTimeMilliseconds();
        entity.UpdatedAt = reservation.UpdatedAt;
        entity.CalculatedCapacityUnits = reservation.CalculatedCapacityUnits;
        entity.CreatedIp = reservation.CreatedIp;
        entity.CreatedUserAgent = reservation.CreatedUserAgent;
        entity.PromotedOrderId = reservation.PromotedOrderId;
        entity.PromotedOrderCode = reservation.PromotedOrderCode;
        entity.PromotedAt = reservation.PromotedAt;
    }

    private static ReservationRecord ToDomain(SchedulingReservationEntity e) => new(
        e.Id,
        e.ClinicCode,
        e.ClinicDisplayName,
        e.MemberId,
        e.MemberLabel,
        e.MemberPinHashFingerprint,
        e.CaseName,
        e.ImpressionDate,
        Enum.Parse<ProductCategory>(e.ProductCategory),
        Enum.Parse<Material>(e.Material),
        DeserializeWorkItems(e.WorkItemsJson),
        e.RequestedDeliveryDate,
        Enum.Parse<ReservationStatus>(e.Status),
        e.Shade,
        e.Notes,
        e.CreatedAt,
        e.UpdatedAt,
        e.CreatedIp,
        e.CreatedUserAgent,
        e.ColorNote,
        e.CalculatedCapacityUnits,
        e.PromotedOrderId,
        e.PromotedOrderCode,
        e.PromotedAt);

    private static string SerializeWorkItems(IReadOnlyList<OrderWorkItem> items) =>
        JsonSerializer.Serialize(items.Select(i => new WorkItemJson(i.ConstructionType, i.ToothStart, i.ToothEnd)), WorkItemsJsonOptions);

    private static IReadOnlyList<OrderWorkItem> DeserializeWorkItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Scheduling reservation is missing work items.");

        var items = JsonSerializer.Deserialize<List<WorkItemJson>>(json, WorkItemsJsonOptions) ?? [];
        if (items.Count == 0)
            throw new InvalidOperationException("Scheduling reservation is missing work items.");
        return items.Select(i => new OrderWorkItem(i.ConstructionType, new ToothRange(i.ToothStart, i.ToothEnd))).ToArray();
    }

    private sealed record WorkItemJson(ConstructionType ConstructionType, int ToothStart, int ToothEnd);
}
