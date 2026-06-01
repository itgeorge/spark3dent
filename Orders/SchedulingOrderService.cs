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
        var rule = _configProvider.Current.FindWorkRule(draft.ProductCategory, draft.WorkType, draft.Material, draft.ConstructionType);
        return await _availability.CalculateMinimumDateAsync(draft.ImpressionDate, rule.MinBusinessDays, ct);
    }

    public async Task<IReadOnlyList<DeliveryDateStatus>> GetDateStatusesAsync(OrderDraft draft, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var minimum = await CalculateMinimumDeliveryDateAsync(draft, ct);
        return await _availability.GetStatusesAsync(start, end, minimum, ct);
    }

    public async Task<OrderRecord> CreateOrderAsync(AuthenticatedActor actor, OrderDraft draft, string ip, string userAgent, CancellationToken ct = default)
    {
        ValidateDraft(draft);
        var minimum = await CalculateMinimumDeliveryDateAsync(draft, ct);
        await _availability.ValidateDeliveryDateAsync(draft.RequestedDeliveryDate, minimum, ct);

        var orderWithoutCode = BuildOrder(actor, draft, ip, userAgent);
        return await CreateWithUniqueCodeAsync(orderWithoutCode, ct);
    }

    public Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default) => _orders.GetOrderByCodeAsync(orderCode, ct);
    public Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default) => _orders.ListOrdersAsync(limit, ct);

    private static void ValidateDraft(OrderDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.CaseName))
            throw new InvalidOperationException("Case name is required.");
        draft.TeethRange.Validate(draft.ConstructionType);
    }

    private OrderRecord BuildOrder(AuthenticatedActor actor, OrderDraft draft, string ip, string userAgent)
    {
        var now = _clock.UtcNow;
        return new OrderRecord(
            0,
            "",
            actor.ClinicCode,
            actor.ClinicDisplayName,
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
            string.IsNullOrWhiteSpace(draft.Shade) ? null : draft.Shade.Trim(),
            string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
            now,
            now,
            ip,
            userAgent);
    }

    private async Task<OrderRecord> CreateWithUniqueCodeAsync(OrderRecord orderWithoutCode, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= _maxOrderCodeAttempts; attempt++)
        {
            var code = _codeGenerator.Generate();
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
