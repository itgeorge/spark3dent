using Utilities;

namespace Orders;

public sealed class SchedulingOrderService
{
    private readonly ISchedulingConfigProvider _configProvider;
    private readonly IOrderRepository _orders;
    private readonly DateAvailabilityService _availability;
    private readonly IOrderCodeGenerator _codeGenerator;
    private readonly IClock _clock;
    private readonly int _maxOrderCodeAttempts;

    public SchedulingOrderService(
        ISchedulingConfigProvider configProvider,
        IOrderRepository orders,
        DateAvailabilityService availability,
        IOrderCodeGenerator codeGenerator,
        IClock clock,
        int maxOrderCodeAttempts = 20)
    {
        _configProvider = configProvider;
        _orders = orders;
        _availability = availability;
        _codeGenerator = codeGenerator;
        _clock = clock;
        _maxOrderCodeAttempts = maxOrderCodeAttempts;
    }

    public async Task<DateOnly> CalculateMinimumDeliveryDateAsync(OrderDraft draft, CancellationToken ct = default)
    {
        ValidateTeethRange(draft);
        var rule = _configProvider.Current.FindWorkRule(draft.ProductCategory, draft.WorkType, draft.Material, draft.ConstructionType);
        return await _availability.CalculateMinimumDateAsync(draft.ImpressionDate, rule.MinBusinessDays, ct);
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
        var minimum = await CalculateMinimumDeliveryDateAsync(draft, ct);
        await _availability.ValidateDeliveryDateAsync(draft.RequestedDeliveryDate, minimum, ct);

        var targetClinic = ResolveTargetClinic(actor, targetClinicCode);
        var orderWithoutCode = BuildOrder(actor, targetClinic, draft, ip, userAgent);
        return await CreateWithUniqueCodeAsync(orderWithoutCode, draft, ct);
    }

    public async Task<OrderRecord> UpdateOrderAsync(AuthenticatedActor actor, string orderCode, OrderDraft draft, CancellationToken ct = default)
    {
        var existing = await GetAuthorizedOrderAsync(actor, orderCode, ct);
        if (existing.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Cancelled orders cannot be modified.");

        ValidateDraft(draft);
        var minimum = await CalculateMinimumDeliveryDateAsync(draft, ct);
        await _availability.ValidateDeliveryDateAsync(draft.RequestedDeliveryDate, minimum, ct);

        var updated = existing with
        {
            CaseName = draft.CaseName.Trim(),
            ImpressionDate = draft.ImpressionDate,
            ProductCategory = draft.ProductCategory,
            WorkType = draft.WorkType,
            Material = draft.Material,
            ConstructionType = draft.ConstructionType,
            ToothStart = draft.TeethRange.Start,
            ToothEnd = draft.TeethRange.End,
            AbutmentTeeth = string.Join(",", draft.TeethRange.DefaultAbutments(draft.ConstructionType)),
            RequestedDeliveryDate = draft.RequestedDeliveryDate,
            Shade = draft.Shade,
            Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
            UpdatedAt = _clock.UtcNow
        };
        return await _orders.UpdateOrderAsync(updated, ct);
    }

    public async Task<OrderRecord> CancelOrderAsync(AuthenticatedActor actor, string orderCode, CancellationToken ct = default)
    {
        var existing = await GetAuthorizedOrderAsync(actor, orderCode, ct);
        if (existing.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order is already cancelled.");
        return await _orders.UpdateOrderAsync(existing with { Status = OrderStatus.Cancelled, UpdatedAt = _clock.UtcNow }, ct);
    }

    public Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default) => _orders.GetOrderByCodeAsync(orderCode, ct);
    public Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default) => _orders.ListOrdersAsync(limit, ct);
    public Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default) =>
        _orders.ListOrdersForClinicAsync(clinicCode, limit, ct);

    public Task<IReadOnlyList<OrderRecord>> ListOrdersForActorAsync(AuthenticatedActor actor, int limit = 100, CancellationToken ct = default) =>
        actor.IsTechnician ? ListOrdersAsync(limit, ct) : ListOrdersForClinicAsync(actor.ClinicCode, limit, ct);

    private static void ValidateDraft(OrderDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.CaseName))
            throw new InvalidOperationException("Case name is required.");
        ValidateTeethRange(draft);
    }

    private static void ValidateTeethRange(OrderDraft draft)
    {
        draft.TeethRange.Validate(draft.ConstructionType);
    }

    private ClinicConfig ResolveTargetClinic(AuthenticatedActor actor, string? targetClinicCode)
    {
        if (!actor.IsTechnician)
        {
            if (!string.IsNullOrWhiteSpace(targetClinicCode) && !string.Equals(targetClinicCode, actor.ClinicCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Clinic users cannot create orders for another clinic.");
            return _configProvider.Current.GetClinic(actor.ClinicCode);
        }

        if (string.IsNullOrWhiteSpace(targetClinicCode))
            throw new InvalidOperationException("Target clinic is required for technician order creation.");
        return _configProvider.Current.GetClinic(targetClinicCode.Trim());
    }

    private async Task<OrderRecord> GetAuthorizedOrderAsync(AuthenticatedActor actor, string orderCode, CancellationToken ct)
    {
        var order = await _orders.GetOrderByCodeAsync(orderCode, ct);
        if (order == null || (!actor.IsTechnician && !string.Equals(order.ClinicCode, actor.ClinicCode, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException("Order not found.");
        return order;
    }

    private OrderRecord BuildOrder(AuthenticatedActor actor, ClinicConfig targetClinic, OrderDraft draft, string ip, string userAgent)
    {
        var now = _clock.UtcNow;
        return new OrderRecord(
            0,
            "",
            targetClinic.Code,
            targetClinic.DisplayName,
            actor.CredentialId,
            actor.CredentialLabel,
            actor.CredentialPinHashFingerprint,
            draft.CaseName.Trim(),
            draft.ImpressionDate,
            draft.ProductCategory,
            draft.WorkType,
            draft.Material,
            draft.ConstructionType,
            draft.TeethRange.Start,
            draft.TeethRange.End,
            string.Join(",", draft.TeethRange.DefaultAbutments(draft.ConstructionType)),
            draft.RequestedDeliveryDate,
            OrderStatus.Created,
            draft.Shade,
            string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
            now,
            now,
            ip,
            userAgent);
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
}
