using System.Text.Json;
using Utilities;

namespace Orders;

public sealed class SchedulingOrderService
{
    private readonly ISchedulingConfigProvider _configProvider;
    private readonly ISchedulingIdentityRepository _identities;
    private readonly IOrderRepository _orders;
    private readonly DateAvailabilityService _availability;
    private readonly IOrderCodeGenerator _codeGenerator;
    private readonly IClock _clock;
    private const int DefaultPageLimit = 50;
    private const int MaxPageLimit = 100;

    private readonly IAuditLog _auditLog;
    private readonly int _maxOrderCodeAttempts;

    public SchedulingOrderService(
        ISchedulingConfigProvider configProvider,
        ISchedulingIdentityRepository identities,
        IOrderRepository orders,
        DateAvailabilityService availability,
        IOrderCodeGenerator codeGenerator,
        IClock clock,
        int maxOrderCodeAttempts = 20,
        IAuditLog? auditLog = null)
    {
        _configProvider = configProvider;
        _identities = identities;
        _orders = orders;
        _availability = availability;
        _codeGenerator = codeGenerator;
        _clock = clock;
        _auditLog = auditLog ?? NoOpAuditLog.Instance;
        _maxOrderCodeAttempts = maxOrderCodeAttempts;
    }

    public async Task<DateOnly> CalculateMinimumDeliveryDateAsync(OrderDraft draft, CancellationToken ct = default)
    {
        ValidateOrderWorkItems(draft);
        var workItems = draft.WorkItems;
        var requiredBusinessDays = workItems.Sum(item =>
        {
            var workType = WorkTypeFor(draft.ProductCategory, draft.Material, item.ConstructionType);
            return _configProvider.Current.FindWorkRule(draft.ProductCategory, workType, draft.Material, item.ConstructionType).MinBusinessDays;
        });
        return await _availability.CalculateMinimumDateAsync(draft.ImpressionDate, requiredBusinessDays, ct);
    }

    public async Task<IReadOnlyList<DeliveryDateStatus>> GetDateStatusesAsync(OrderDraft draft, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var minimum = await CalculateMinimumDeliveryDateAsync(draft, ct);
        return await _availability.GetStatusesAsync(start, end, minimum, ct);
    }

    public Task<OrderRecord> CreateOrderAsync(AuthenticatedActor actor, OrderDraft draft, string ip, string userAgent, CancellationToken ct = default) =>
        CreateOrderAsync(actor, draft, ip, userAgent, targetClinicCode: null, ct);

    public async Task<OrderRecord> CreateOrderAsync(AuthenticatedActor actor, OrderDraft draft, string ip, string userAgent, string? targetClinicCode, CancellationToken ct = default)
    {
        ValidateDraft(draft);
        await ValidateDeliveryDateForActorAsync(actor, draft, ct);

        var targetClinic = await ResolveTargetClinicAsync(actor, targetClinicCode, ct);
        var orderWithoutCode = BuildOrder(actor, targetClinic, draft, ip, userAgent);
        var created = await CreateWithUniqueCodeAsync(orderWithoutCode, draft, ct);
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
        return created;
    }

    public Task<OrderRecord> UpdateOrderAsync(AuthenticatedActor actor, string orderCode, OrderDraft draft, CancellationToken ct = default) =>
        UpdateOrderAsync(actor, orderCode, draft, ip: null, userAgent: null, ct);

    public async Task<OrderRecord> UpdateOrderAsync(AuthenticatedActor actor, string orderCode, OrderDraft draft, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var existing = await GetAuthorizedOrderAsync(actor, orderCode, ct);
        if (existing.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Cancelled orders cannot be modified.");

        ValidateDraft(draft);
        await ValidateDeliveryDateForActorAsync(actor, draft, ct);

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
            UpdatedAt = _clock.UtcNow
        };
        var saved = await _orders.UpdateOrderAsync(updated, ct);
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

    private async Task ValidateDeliveryDateForActorAsync(AuthenticatedActor actor, OrderDraft draft, CancellationToken ct)
    {
        var minimum = await CalculateMinimumDeliveryDateAsync(draft, ct);
        var status = await _availability.GetStatusAsync(draft.RequestedDeliveryDate, minimum, ct);
        if (status.IsSelectable) return;
        if (actor.IsLab && status.IsBeforeMinimum && !status.IsClosed && !status.IsFirstBusinessDayAfterClosure) return;
        throw new InvalidOperationException($"Delivery date {draft.RequestedDeliveryDate:yyyy-MM-dd} is not available: {status.Reason}.");
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

    private static WorkType WorkTypeFor(ProductCategory productCategory, Material material, ConstructionType constructionType)
    {
        if (material == Material.Pmma || productCategory == ProductCategory.Temporary)
            return WorkType.TemporaryCrownBridge;
        return constructionType == ConstructionType.Bridge ? WorkType.Bridge : WorkType.Crown;
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

    private async Task<OrderRecord> GetAuthorizedOrderAsync(AuthenticatedActor actor, string orderCode, CancellationToken ct)
    {
        var order = await _orders.GetOrderByCodeAsync(orderCode, ct);
        if (order == null || !CanActorSeeOrder(actor, order))
            throw new KeyNotFoundException("Order not found.");
        return order;
    }

    private OrderRecord BuildOrder(AuthenticatedActor actor, SchedulingClinic targetClinic, OrderDraft draft, string ip, string userAgent)
    {
        var now = _clock.UtcNow;
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
            string.IsNullOrWhiteSpace(draft.ColorNote) ? null : draft.ColorNote.Trim());
    }

    private async Task<OrderRecord> CreateWithUniqueCodeAsync(OrderRecord orderWithoutCode, OrderDraft draft, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= _maxOrderCodeAttempts; attempt++)
        {
            var code = _codeGenerator.Generate(draft);
            try
            {
                return await _orders.CreateOrderAsync(orderWithoutCode with { OrderCode = code }, ct);
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
