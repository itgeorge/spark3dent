using System.Text.Json;
using Utilities;

namespace Orders;

public sealed class SchedulingOrderService
{
    private readonly ISchedulingIdentityRepository _identities;
    private readonly IOrderRepository _orders;
    private readonly DateAvailabilityService _availability;
    private readonly DeadlineRecommendationService _deadlineRecommendations;
    private readonly ISchedulingWriteTransaction _writeTransaction;
    private readonly IOrderCodeGenerator _codeGenerator;
    private readonly IClock _clock;
    private const int DefaultPageLimit = 50;
    private const int MaxPageLimit = 100;

    private readonly IAuditLog _auditLog;
    private readonly IDeadlineRecommendationLogRepository _deadlineRecommendationLogs;
    private readonly int _maxOrderCodeAttempts;

    public SchedulingOrderService(
        ISchedulingIdentityRepository identities,
        IOrderRepository orders,
        DateAvailabilityService availability,
        DeadlineRecommendationService deadlineRecommendations,
        ISchedulingWriteTransaction writeTransaction,
        IOrderCodeGenerator codeGenerator,
        IClock clock,
        int maxOrderCodeAttempts = 20,
        IAuditLog? auditLog = null,
        IDeadlineRecommendationLogRepository? deadlineRecommendationLogs = null)
    {
        _identities = identities;
        _orders = orders;
        _availability = availability;
        _deadlineRecommendations = deadlineRecommendations;
        _writeTransaction = writeTransaction;
        _codeGenerator = codeGenerator;
        _clock = clock;
        _auditLog = auditLog ?? NoOpAuditLog.Instance;
        _deadlineRecommendationLogs = deadlineRecommendationLogs ?? NoOpDeadlineRecommendationLogRepository.Instance;
        _maxOrderCodeAttempts = maxOrderCodeAttempts;
    }

    public Task<DateOnly> CalculateMinimumDeliveryDateAsync(OrderDraft draft, CancellationToken ct = default) =>
        CalculateMinimumDeliveryDateAsync(draft, _clock.UtcNow, ct);

    public async Task<DateOnly> CalculateMinimumDeliveryDateAsync(OrderDraft draft, DateTimeOffset impressionTimestampUtc, CancellationToken ct = default)
    {
        ValidateOrderWorkItems(draft);
        var recommendation = await _deadlineRecommendations.RecommendAsync(
            new OrderSchedulingInput(draft.Material, draft.WorkItems, impressionTimestampUtc),
            ct);
        return recommendation.EarliestSelectableDeadline;
    }

    public Task<IReadOnlyList<DeliveryDateStatus>> GetDateStatusesAsync(OrderDraft draft, DateOnly start, DateOnly end, CancellationToken ct = default) =>
        GetDateStatusesAsync(draft, start, end, _clock.UtcNow, excludedOrderId: null, ct);

    public Task<IReadOnlyList<DeliveryDateStatus>> GetDateStatusesAsync(OrderDraft draft, DateOnly start, DateOnly end, DateTimeOffset impressionTimestampUtc, CancellationToken ct = default) =>
        GetDateStatusesAsync(draft, start, end, impressionTimestampUtc, excludedOrderId: null, ct);

    public async Task<IReadOnlyList<DeliveryDateStatus>> GetDateStatusesAsync(
        OrderDraft draft,
        DateOnly start,
        DateOnly end,
        DateTimeOffset impressionTimestampUtc,
        long? excludedOrderId,
        CancellationToken ct = default)
    {
        ValidateOrderWorkItems(draft);
        var result = await _deadlineRecommendations.GetCapacityAwareDateStatusesAsync(
            new OrderSchedulingInput(draft.Material, draft.WorkItems, impressionTimestampUtc, excludedOrderId),
            start,
            end,
            orderRepositoryOverride: null,
            ct);
        return result.Statuses;
    }

    public Task<OrderRecord> CreateOrderAsync(AuthenticatedActor actor, OrderDraft draft, string ip, string userAgent, CancellationToken ct = default) =>
        CreateOrderAsync(actor, draft, ip, userAgent, targetClinicCode: null, ct);

    public async Task<OrderRecord> CreateOrderAsync(AuthenticatedActor actor, OrderDraft draft, string ip, string userAgent, string? targetClinicCode, CancellationToken ct = default)
    {
        ValidateDraft(draft);

        var targetClinic = await ResolveTargetClinicAsync(actor, targetClinicCode, ct);
        var createTimestamp = _clock.UtcNow;
        var createResult = await _writeTransaction.ExecuteAsync(async txOrders =>
        {
            var validation = await ValidateDeliveryDateForActorWithAuditAsync(actor, draft, createTimestamp, excludedOrderId: null, txOrders, ct);
            var orderWithoutCode = BuildOrder(actor, targetClinic, draft, ip, userAgent, validation.Validation.OrderCapacityUnits, createTimestamp);
            var createdOrder = await CreateWithUniqueCodeAsync(orderWithoutCode, draft, txOrders, ct);
            return (Order: createdOrder, validation.Audit);
        }, ct);
        var created = createResult.Order;
        await AppendOrderAuditAsync(
            actor,
            "OrderCreated",
            created,
            ip,
            userAgent,
            new
            {
                orderCode = created.OrderCode,
                targetClinicCode = created.ClinicCode,
                targetClinicDisplayName = created.ClinicDisplayName,
                caseName = created.CaseName,
                colorNote = created.ColorNote,
                requestedDeliveryDate = created.RequestedDeliveryDate,
                status = created.Status.ToString(),
                workItems = WorkItemsAudit(created.WorkItems),
                totalToothCount = OrderWorkItem.AllTeeth(created.WorkItems).Length
            },
            ct);
        await PersistDeadlineRecommendationLogAsync(actor, created, createResult.Audit, ct);
        return created;
    }

    public Task<OrderRecord> UpdateOrderAsync(AuthenticatedActor actor, string orderCode, OrderDraft draft, CancellationToken ct = default) =>
        UpdateOrderAsync(actor, orderCode, draft, ip: null, userAgent: null, ct);

    public async Task<OrderRecord> UpdateOrderAsync(AuthenticatedActor actor, string orderCode, OrderDraft draft, string? ip, string? userAgent, CancellationToken ct = default)
    {
        ValidateDraft(draft);
        var result = await _writeTransaction.ExecuteAsync(async txOrders =>
        {
            var existing = await GetAuthorizedOrderAsync(actor, orderCode, txOrders, ct);
            if (existing.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException("Cancelled orders cannot be modified.");

            var validation = await ValidateDeliveryDateForActorWithAuditAsync(actor, draft, existing.CreatedAt, existing.Id, txOrders, ct);

            var updated = existing with
            {
                CaseName = draft.CaseName.Trim(),
                ImpressionDate = draft.ImpressionDate,
                ProductCategory = draft.ProductCategory,
                Material = draft.Material,
                WorkItems = draft.WorkItems,
                RequestedDeliveryDate = draft.RequestedDeliveryDate,
                Shade = draft.Shade,
                Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
                ColorNote = string.IsNullOrWhiteSpace(draft.ColorNote) ? null : draft.ColorNote.Trim(),
                CalculatedCapacityUnits = validation.Validation.OrderCapacityUnits,
                UpdatedAt = _clock.UtcNow
            };
            var saved = await txOrders.UpdateOrderAsync(updated, ct);
            return (Existing: existing, Saved: saved, validation.Audit);
        }, ct);
        var existing = result.Existing;
        var saved = result.Saved;
        await AppendOrderAuditAsync(
            actor,
            "OrderUpdated",
            saved,
            ip,
            userAgent,
            new
            {
                orderCode = saved.OrderCode,
                targetClinicCode = saved.ClinicCode,
                targetClinicDisplayName = saved.ClinicDisplayName,
                changedFields = ChangedFields(existing, saved),
                oldRequestedDeliveryDate = existing.RequestedDeliveryDate == saved.RequestedDeliveryDate ? (DateOnly?)null : existing.RequestedDeliveryDate,
                newRequestedDeliveryDate = existing.RequestedDeliveryDate == saved.RequestedDeliveryDate ? (DateOnly?)null : saved.RequestedDeliveryDate,
                oldWorkItems = WorkItemsEqual(existing.WorkItems, saved.WorkItems) ? null : WorkItemsAudit(existing.WorkItems),
                newWorkItems = WorkItemsEqual(existing.WorkItems, saved.WorkItems) ? null : WorkItemsAudit(saved.WorkItems)
            },
            ct);
        await PersistDeadlineRecommendationLogAsync(actor, saved, result.Audit, ct);
        return saved;
    }

    public Task<OrderRecord> CancelOrderAsync(AuthenticatedActor actor, string orderCode, CancellationToken ct = default) =>
        CancelOrderAsync(actor, orderCode, ip: null, userAgent: null, ct);

    public async Task<OrderRecord> CancelOrderAsync(AuthenticatedActor actor, string orderCode, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var existing = await GetAuthorizedOrderAsync(actor, orderCode, ct);
        if (existing.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order is already cancelled.");
        var cancelled = await _orders.UpdateOrderAsync(existing with { Status = OrderStatus.Cancelled, UpdatedAt = _clock.UtcNow }, ct);
        await AppendOrderAuditAsync(
            actor,
            "OrderCancelled",
            cancelled,
            ip,
            userAgent,
            new
            {
                orderCode = cancelled.OrderCode,
                targetClinicCode = cancelled.ClinicCode,
                targetClinicDisplayName = cancelled.ClinicDisplayName,
                previousStatus = existing.Status.ToString(),
                newStatus = cancelled.Status.ToString()
            },
            ct);
        return cancelled;
    }

    public Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default) => _orders.GetOrderByCodeAsync(orderCode, ct);
    public Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default) => _orders.ListOrdersAsync(limit, ct);
    public Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default) =>
        _orders.ListOrdersForClinicAsync(clinicCode, limit, ct);

    public Task<IReadOnlyList<OrderRecord>> ListOrdersForActorAsync(AuthenticatedActor actor, int limit = 100, CancellationToken ct = default) =>
        actor.IsLab ? ListOrdersAsync(limit, ct) : ListOrdersForClinicAsync(actor.OrganizationCode, limit, ct);

    public Task<OrderPage> ListOrdersPageForActorAsync(AuthenticatedActor actor, int? limit = null, string? cursor = null, CancellationToken ct = default)
    {
        var decoded = OrderCursorCodec.Decode(cursor);
        return _orders.ListOrdersPageAsync(actor.IsLab ? null : actor.OrganizationCode, ClampPageLimit(limit), decoded, ct);
    }

    public async Task<OrderFindResult> FindOrderContextForActorAsync(AuthenticatedActor actor, string code, int? limit = null, CancellationToken ct = default)
    {
        var normalized = NormalizeOrderCodeInput(code);
        if (normalized.Length == 0)
            throw new KeyNotFoundException("Order not found.");

        var order = await _orders.GetOrderByCodeAsync(normalized, ct);
        if (order != null)
        {
            if (!CanActorSeeOrder(actor, order))
                throw new KeyNotFoundException("Order not found.");
        }
        else if (CanSearchShortenedCode(normalized))
        {
            var matches = await _orders.FindOrdersByCodeSuffixAsync(actor.IsLab ? null : actor.OrganizationCode, normalized, 2, ct);
            if (matches.Count > 1)
                throw new AmbiguousOrderCodeException("Multiple orders match this code; enter the full order code.");
            order = matches.SingleOrDefault();
        }

        if (order == null)
            throw new KeyNotFoundException("Order not found.");

        var page = await _orders.ListOrdersPageContainingOrderAsync(actor.IsLab ? null : actor.OrganizationCode, order, ClampPageLimit(limit), ct);
        var listRecommended = order.Status == OrderStatus.Cancelled;
        return new OrderFindResult(
            order,
            page,
            listRecommended,
            listRecommended ? "Cancelled orders are only visible in list view." : null);
    }

    public Task<IReadOnlyList<OrderRecord>> ListCalendarOrdersAsync(AuthenticatedActor actor, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (start > end)
            throw new InvalidOperationException("Calendar start date must be before or equal to end date.");
        return _orders.ListActiveOrdersForCalendarAsync(actor.IsLab ? null : actor.OrganizationCode, start, end, ct);
    }

    private static int ClampPageLimit(int? limit) => Math.Clamp(limit ?? DefaultPageLimit, 1, MaxPageLimit);

    private static string NormalizeOrderCodeInput(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static bool CanSearchShortenedCode(string code) => !(code.Length >= 3 && char.IsDigit(code[0]) && char.IsDigit(code[1]) && code[2] == '-');

    private static bool CanActorSeeOrder(AuthenticatedActor actor, OrderRecord order) =>
        actor.IsLab || string.Equals(order.ClinicCode, actor.OrganizationCode, StringComparison.OrdinalIgnoreCase);

    private Task AppendOrderAuditAsync(AuthenticatedActor actor, string operation, OrderRecord order, string? ip, string? userAgent, object metadata, CancellationToken ct)
    {
        var auditEvent = new AuditEvent(
            0,
            "Scheduling",
            operation,
            "SchedulingOrder",
            order.OrderCode,
            order.CaseName,
            actor.OrganizationType.ToString(),
            actor.OrganizationCode,
            actor.MemberId,
            actor.MemberLabel,
            actor.SessionId,
            _clock.UtcNow,
            string.IsNullOrWhiteSpace(ip) ? null : ip,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            JsonSerializer.Serialize(metadata));
        return _auditLog.AppendAsync(auditEvent, ct);
    }

    private Task PersistDeadlineRecommendationLogAsync(AuthenticatedActor actor, OrderRecord order, DeadlineRecommendationAudit audit, CancellationToken ct)
    {
        var log = new DeadlineRecommendationLog(
            0,
            order.Id,
            order.OrderCode,
            _clock.UtcNow,
            actor.OrganizationType.ToString(),
            actor.OrganizationCode,
            actor.MemberId,
            actor.MemberLabel,
            audit.ImpressionTimestampUtc,
            audit.EffectiveIntakeBusinessDate,
            audit.CutoffTimeUsed,
            audit.Material,
            audit.ToothCount,
            audit.LeadTimeBusinessDaysUsed,
            audit.FixedLeadTimeBusinessDaysUsed,
            audit.ExtraLeadTimeBusinessDaysUsed,
            audit.TeethPerExtraLeadDayUsed,
            audit.CapacityUnitsPerToothUsed,
            audit.CalculatedOrderCapacityUnits,
            audit.MinimumDeadlineDateFromLeadTime,
            audit.FinalRecommendedDeadlineDate,
            audit.SelectedDeadlineDate,
            audit.SearchStartedAtDate,
            audit.SearchEndedAtDate,
            audit.SearchLimitDate,
            audit.ResultStatus,
            audit.FailureReason,
            audit.CandidateChecksJson,
            audit.ConfigSnapshotJson);
        return _deadlineRecommendationLogs.AddAsync(log, ct);
    }

    private static string[] ChangedFields(OrderRecord oldOrder, OrderRecord newOrder)
    {
        var changed = new List<string>();
        if (oldOrder.CaseName != newOrder.CaseName) changed.Add(nameof(OrderRecord.CaseName));
        if (oldOrder.ImpressionDate != newOrder.ImpressionDate) changed.Add(nameof(OrderRecord.ImpressionDate));
        if (oldOrder.ProductCategory != newOrder.ProductCategory) changed.Add(nameof(OrderRecord.ProductCategory));
        if (oldOrder.Material != newOrder.Material) changed.Add(nameof(OrderRecord.Material));
        if (!WorkItemsEqual(oldOrder.WorkItems, newOrder.WorkItems)) changed.Add(nameof(OrderRecord.WorkItems));
        if (oldOrder.RequestedDeliveryDate != newOrder.RequestedDeliveryDate) changed.Add(nameof(OrderRecord.RequestedDeliveryDate));
        if (oldOrder.Shade != newOrder.Shade) changed.Add(nameof(OrderRecord.Shade));
        if (oldOrder.Notes != newOrder.Notes) changed.Add(nameof(OrderRecord.Notes));
        if (oldOrder.ColorNote != newOrder.ColorNote) changed.Add(nameof(OrderRecord.ColorNote));
        return changed.ToArray();
    }

    private Task<DeadlineValidationResult> ValidateDeliveryDateForActorAsync(AuthenticatedActor actor, OrderDraft draft, CancellationToken ct) =>
        ValidateDeliveryDateForActorAsync(actor, draft, _clock.UtcNow, excludedOrderId: null, orderRepositoryOverride: null, ct: ct);

    private Task<DeadlineValidationResult> ValidateDeliveryDateForActorAsync(AuthenticatedActor actor, OrderDraft draft, DateTimeOffset impressionTimestampUtc, CancellationToken ct) =>
        ValidateDeliveryDateForActorAsync(actor, draft, impressionTimestampUtc, excludedOrderId: null, orderRepositoryOverride: null, ct: ct);

    private async Task<DeadlineValidationResult> ValidateDeliveryDateForActorAsync(
        AuthenticatedActor actor,
        OrderDraft draft,
        DateTimeOffset impressionTimestampUtc,
        long? excludedOrderId,
        IOrderRepository? orderRepositoryOverride,
        CancellationToken ct) =>
        (await ValidateDeliveryDateForActorWithAuditAsync(actor, draft, impressionTimestampUtc, excludedOrderId, orderRepositoryOverride, ct)).Validation;

    private async Task<DeadlineValidationWithAuditResult> ValidateDeliveryDateForActorWithAuditAsync(
        AuthenticatedActor actor,
        OrderDraft draft,
        DateTimeOffset impressionTimestampUtc,
        long? excludedOrderId,
        IOrderRepository? orderRepositoryOverride,
        CancellationToken ct)
    {
        var result = await _deadlineRecommendations.ValidateRequestedDateWithAuditAsync(
            new OrderSchedulingInput(draft.Material, draft.WorkItems, impressionTimestampUtc, excludedOrderId),
            draft.RequestedDeliveryDate,
            orderRepositoryOverride,
            ct);
        var validation = result.Validation;
        if (validation.Status.IsSelectable) return result;
        if (actor.IsLab
            && validation.Status.IsBeforeMinimum
            && !validation.Status.IsClosed
            && !validation.Status.IsFirstBusinessDayAfterClosure
            && !validation.Status.IsDailyCapacityExceeded
            && !validation.Status.IsWeeklyCapacityExceeded)
            return result;
        throw new InvalidOperationException($"Delivery date {draft.RequestedDeliveryDate:yyyy-MM-dd} is not available: {validation.Status.Reason}.");
    }

    private static void ValidateDraft(OrderDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.CaseName))
            throw new InvalidOperationException("Case name is required.");
        ValidateOrderWorkItems(draft);
    }

    private static void ValidateOrderWorkItems(OrderDraft draft)
    {
        if (draft.WorkItems == null)
            throw new InvalidOperationException("At least one order work item is required.");
        OrderWorkItem.ValidateAll(draft.WorkItems);
    }

    private static bool WorkItemsEqual(IReadOnlyList<OrderWorkItem> left, IReadOnlyList<OrderWorkItem> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.ConstructionType == pair.Second.ConstructionType &&
            pair.First.ToothStart == pair.Second.ToothStart &&
            pair.First.ToothEnd == pair.Second.ToothEnd);

    private static object[] WorkItemsAudit(IReadOnlyList<OrderWorkItem> items) => items.Select(WorkItemAudit).ToArray();

    private static object WorkItemAudit(OrderWorkItem item) => new
    {
        constructionType = item.ConstructionType.ToString(),
        toothStart = item.ToothStart,
        toothEnd = item.ToothEnd,
        teeth = item.Teeth
    };

    private async Task<SchedulingClinic> ResolveTargetClinicAsync(AuthenticatedActor actor, string? targetClinicCode, CancellationToken ct)
    {
        if (!actor.IsLab)
        {
            if (!string.IsNullOrWhiteSpace(targetClinicCode) && !string.Equals(targetClinicCode, actor.OrganizationCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Clinic users cannot create orders for another clinic.");
            return await RequireActiveClinicAsync(actor.OrganizationCode, ct);
        }

        if (string.IsNullOrWhiteSpace(targetClinicCode))
            throw new InvalidOperationException("Target clinic is required for lab order creation.");
        return await RequireActiveClinicAsync(targetClinicCode.Trim(), ct);
    }

    private Task<OrderRecord> GetAuthorizedOrderAsync(AuthenticatedActor actor, string orderCode, CancellationToken ct) =>
        GetAuthorizedOrderAsync(actor, orderCode, _orders, ct);

    private async Task<OrderRecord> GetAuthorizedOrderAsync(AuthenticatedActor actor, string orderCode, IOrderRepository orders, CancellationToken ct)
    {
        var order = await orders.GetOrderByCodeAsync(orderCode, ct);
        if (order == null || !CanActorSeeOrder(actor, order))
            throw new KeyNotFoundException("Order not found.");
        return order;
    }

    private OrderRecord BuildOrder(AuthenticatedActor actor, SchedulingClinic targetClinic, OrderDraft draft, string ip, string userAgent, decimal calculatedCapacityUnits, DateTimeOffset now)
    {
        return new OrderRecord(
            0,
            "",
            targetClinic.Code,
            targetClinic.DisplayName,
            actor.MemberId,
            actor.MemberLabel,
            actor.MemberPinHashFingerprint,
            draft.CaseName.Trim(),
            draft.ImpressionDate,
            draft.ProductCategory,
            draft.Material,
            draft.WorkItems,
            draft.RequestedDeliveryDate,
            OrderStatus.Created,
            draft.Shade,
            string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
            now,
            now,
            ip,
            userAgent,
            string.IsNullOrWhiteSpace(draft.ColorNote) ? null : draft.ColorNote.Trim(),
            calculatedCapacityUnits);
    }

    private async Task<OrderRecord> CreateWithUniqueCodeAsync(OrderRecord orderWithoutCode, OrderDraft draft, IOrderRepository orders, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= _maxOrderCodeAttempts; attempt++)
        {
            var code = _codeGenerator.Generate(draft);
            try
            {
                return await orders.CreateOrderAsync(orderWithoutCode with { OrderCode = code }, ct);
            }
            catch (DuplicateOrderCodeException) when (attempt < _maxOrderCodeAttempts)
            {
                // Another request won this generated code. Generate a new one and retry.
            }
        }

        throw new InvalidOperationException("Could not allocate a unique order code.");
    }

    private async Task<SchedulingClinic> RequireActiveClinicAsync(string clinicCode, CancellationToken ct)
    {
        var clinic = await _identities.GetClinicAsync(clinicCode, includeInactive: false, ct);
        return clinic ?? throw new InvalidOperationException("Clinic not found or inactive.");
    }
}
