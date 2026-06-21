namespace Orders;

public sealed record OrderSchedulingInput(
    Material Material,
    IReadOnlyList<OrderWorkItem> WorkItems,
    DateTimeOffset ImpressionTimestampUtc,
    long? ExcludedOrderId = null);

public sealed record DeadlineRecommendationResult(
    DateOnly EffectiveIntakeBusinessDate,
    int LeadTimeBusinessDays,
    DateOnly PostLeadTimeCandidateDate,
    DateOnly EarliestSelectableDeadline);

public sealed record DeadlineDateStatusesResult(
    DateOnly MinimumDate,
    DateOnly? RecommendedDate,
    decimal OrderCapacityUnits,
    IReadOnlyList<DeliveryDateStatus> Statuses);

public sealed class DeadlineRecommendationService
{
    private static readonly TimeOnly IntakeCutoff = new(11, 0);
    private const int SelectableDeadlineSearchLimitDays = 60;

    private readonly DateAvailabilityService _availability;
    private readonly IMaterialSchedulingConfigProvider _materialConfigs;
    private readonly ISchedulingCapacityConfigProvider _capacityConfigs;
    private readonly IOrderRepository _orders;

    public DeadlineRecommendationService(
        DateAvailabilityService availability,
        IMaterialSchedulingConfigProvider materialConfigs,
        ISchedulingCapacityConfigProvider capacityConfigs,
        IOrderRepository orders)
    {
        _availability = availability;
        _materialConfigs = materialConfigs;
        _capacityConfigs = capacityConfigs;
        _orders = orders;
    }

    public async Task<DeadlineRecommendationResult> RecommendAsync(OrderSchedulingInput input, CancellationToken ct = default)
    {
        OrderWorkItem.ValidateAll(input.WorkItems);

        var effectiveIntakeDate = await ResolveEffectiveIntakeBusinessDateAsync(input.ImpressionTimestampUtc, ct);
        var leadTimeDays = await CalculateLeadTimeBusinessDaysAsync(input.Material, input.WorkItems, ct);
        var postLeadTimeCandidate = await CalculatePostLeadTimeCandidateAsync(effectiveIntakeDate, leadTimeDays, ct);
        var earliestSelectable = await AdvanceToSelectableDeadlineAsync(postLeadTimeCandidate, ct);

        return new DeadlineRecommendationResult(effectiveIntakeDate, leadTimeDays, postLeadTimeCandidate, earliestSelectable);
    }

    public async Task<DateOnly> RecommendCapacityAwareDateAsync(OrderSchedulingInput input, CancellationToken ct = default)
    {
        var minimum = (await RecommendAsync(input, ct)).EarliestSelectableDeadline;
        var orderCapacityUnits = await CalculateCapacityUnitsAsync(input.Material, input.WorkItems, ct);
        return await FindRecommendedDateAsync(minimum, orderCapacityUnits, input.ExcludedOrderId, ct);
    }

    public async Task<DeadlineDateStatusesResult> GetCapacityAwareDateStatusesAsync(
        OrderSchedulingInput input,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default)
    {
        if (end < start)
            throw new InvalidOperationException("End date must be on or after start date.");

        var minimum = (await RecommendAsync(input, ct)).EarliestSelectableDeadline;
        var orderCapacityUnits = await CalculateCapacityUnitsAsync(input.Material, input.WorkItems, ct);
        var statuses = new List<DeliveryDateStatus>();
        for (var date = start; date <= end; date = date.AddDays(1))
            statuses.Add(await EvaluateDateAsync(date, minimum, orderCapacityUnits, input.ExcludedOrderId, ct));

        DateOnly? recommendedDate = null;
        try
        {
            recommendedDate = await FindRecommendedDateAsync(minimum, orderCapacityUnits, input.ExcludedOrderId, ct);
        }
        catch (InvalidOperationException)
        {
            // Statuses are still useful even when no recommendation exists inside the search window.
        }

        return new DeadlineDateStatusesResult(minimum, recommendedDate, orderCapacityUnits, statuses);
    }

    public async Task<DeadlineValidationResult> ValidateRequestedDateAsync(
        OrderSchedulingInput input,
        DateOnly requestedDate,
        CancellationToken ct = default)
    {
        var minimum = (await RecommendAsync(input, ct)).EarliestSelectableDeadline;
        var orderCapacityUnits = await CalculateCapacityUnitsAsync(input.Material, input.WorkItems, ct);
        var status = await EvaluateDateAsync(requestedDate, minimum, orderCapacityUnits, input.ExcludedOrderId, ct);

        DateOnly? recommendedDate = null;
        var failedRules = status.GetFailedRules().ToList();
        try
        {
            recommendedDate = await FindRecommendedDateAsync(minimum, orderCapacityUnits, input.ExcludedOrderId, ct);
        }
        catch (InvalidOperationException)
        {
            failedRules.Add(DeadlineValidationRule.SearchFailure);
        }

        return new DeadlineValidationResult(minimum, recommendedDate, status, orderCapacityUnits, failedRules);
    }

    public async Task<decimal> CalculateCapacityUnitsAsync(Material material, IReadOnlyList<OrderWorkItem> workItems, CancellationToken ct = default)
    {
        OrderWorkItem.ValidateAll(workItems);
        var config = await _materialConfigs.GetAsync(material, ct);
        ValidateMaterialConfig(config);
        return OrderWorkItem.AllTeeth(workItems).Length * config.CapacityUnitsPerTooth;
    }

    public async Task<int> CalculateLeadTimeBusinessDaysAsync(Material material, IReadOnlyList<OrderWorkItem> workItems, CancellationToken ct = default)
    {
        var config = await _materialConfigs.GetAsync(material, ct);
        ValidateMaterialConfig(config);
        if (!UsesToothCountExtraLeadTime(material))
            return config.FixedLeadTimeBusinessDays;

        var teethPerExtraLeadDay = config.TeethPerExtraLeadDay!.Value;
        var distinctToothCount = OrderWorkItem.AllTeeth(workItems).Length;
        var extraLeadDays = (distinctToothCount + teethPerExtraLeadDay - 1) / teethPerExtraLeadDay;
        return config.FixedLeadTimeBusinessDays + extraLeadDays;
    }

    private static bool UsesToothCountExtraLeadTime(Material material) =>
        material is Material.Pfm or Material.PfzLayeredZrCrown;

    private static void ValidateMaterialConfig(MaterialSchedulingConfig config)
    {
        if (!config.IsActive)
            throw new InvalidOperationException($"Material scheduling config for {config.Material} is inactive.");
        if (config.FixedLeadTimeBusinessDays <= 0)
            throw new InvalidOperationException($"Material scheduling config for {config.Material} must have positive fixed lead-time business days.");
        if (config.CapacityUnitsPerTooth <= 0)
            throw new InvalidOperationException($"Material scheduling config for {config.Material} must have positive capacity units per tooth.");
        if (UsesToothCountExtraLeadTime(config.Material) && (config.TeethPerExtraLeadDay == null || config.TeethPerExtraLeadDay <= 0))
            throw new InvalidOperationException($"Material scheduling config for {config.Material} must have positive teeth per extra lead day.");
    }

    private static void ValidateCapacityConfig(SchedulingCapacityConfig config)
    {
        if (config.DailyCapacityUnits <= 0)
            throw new InvalidOperationException($"Scheduling capacity config for {config.ActiveFromDate:yyyy-MM-dd} must have positive daily capacity units.");
        if (config.WeeklyCapacityUnits <= 0)
            throw new InvalidOperationException($"Scheduling capacity config for {config.ActiveFromDate:yyyy-MM-dd} must have positive weekly capacity units.");
    }

    public async Task<DateOnly> ResolveEffectiveIntakeBusinessDateAsync(DateTimeOffset impressionTimestampUtc, CancellationToken ct = default)
    {
        var local = LabTimeZone.ToLabLocal(impressionTimestampUtc);
        var localDate = DateOnly.FromDateTime(local.DateTime);
        var localTime = TimeOnly.FromDateTime(local.DateTime);

        if (!await _availability.IsClosedAsync(localDate, ct) && localTime <= IntakeCutoff)
            return localDate;

        return await NextBusinessDateAsync(localDate.AddDays(1), ct);
    }

    private async Task<DateOnly> CalculatePostLeadTimeCandidateAsync(DateOnly effectiveIntakeDate, int leadTimeBusinessDays, CancellationToken ct)
    {
        if (leadTimeBusinessDays <= 0)
            throw new InvalidOperationException("Lead-time business days must be positive.");

        var counted = 0;
        var current = effectiveIntakeDate;
        while (true)
        {
            if (!await _availability.IsClosedAsync(current, ct))
            {
                counted++;
                if (counted == leadTimeBusinessDays)
                    return current.AddDays(1);
            }

            current = current.AddDays(1);
        }
    }

    private async Task<DateOnly> AdvanceToSelectableDeadlineAsync(DateOnly candidate, CancellationToken ct)
    {
        var searchLimit = candidate.AddDays(SelectableDeadlineSearchLimitDays);
        for (var current = candidate; current <= searchLimit; current = current.AddDays(1))
        {
            if (await _availability.CanSelectDeadlineAsync(current, ct))
                return current;
        }

        throw new InvalidOperationException("No selectable deadline found within 60 calendar days.");
    }

    private async Task<DeliveryDateStatus> EvaluateDateAsync(
        DateOnly date,
        DateOnly minimumDate,
        decimal orderCapacityUnits,
        long? excludedOrderId,
        CancellationToken ct)
    {
        var baseStatus = await _availability.GetStatusAsync(date, minimumDate, ct);
        if (baseStatus.IsClosed || baseStatus.IsFirstBusinessDayAfterClosure)
            return baseStatus with { OrderCapacityUnits = orderCapacityUnits };

        var capacityConfig = await _capacityConfigs.GetForDateAsync(date, ct);
        ValidateCapacityConfig(capacityConfig);
        var usage = await GetCapacityUsageAsync(date, excludedOrderId, ct);
        var isDailyCapacityExceeded = usage.DailyUsed + orderCapacityUnits > capacityConfig.DailyCapacityUnits;
        var isWeeklyCapacityExceeded = usage.WeeklyUsed + orderCapacityUnits > capacityConfig.WeeklyCapacityUnits;
        var reason = baseStatus.Reason ?? GetCapacityReason(isDailyCapacityExceeded, isWeeklyCapacityExceeded);
        var isSelectable = baseStatus.IsSelectable && !isDailyCapacityExceeded && !isWeeklyCapacityExceeded;

        return new DeliveryDateStatus(
            date,
            baseStatus.IsClosed,
            baseStatus.IsFirstBusinessDayAfterClosure,
            baseStatus.IsBeforeMinimum,
            isSelectable,
            reason,
            isDailyCapacityExceeded,
            isWeeklyCapacityExceeded,
            orderCapacityUnits,
            usage.DailyUsed,
            usage.WeeklyUsed,
            capacityConfig.DailyCapacityUnits,
            capacityConfig.WeeklyCapacityUnits);
    }

    private async Task<CapacityUsage> GetCapacityUsageAsync(DateOnly date, long? excludedOrderId, CancellationToken ct)
    {
        var (weekStart, weekEnd) = SchedulingWeek.GetRange(date);
        var orders = await _orders.ListActiveOrdersByDeadlineRangeAsync(weekStart, weekEnd, ct);
        var materialCache = new Dictionary<Material, MaterialSchedulingConfig>();
        decimal dailyUsed = 0;
        decimal weeklyUsed = 0;
        foreach (var order in orders)
        {
            if (excludedOrderId.HasValue && order.Id == excludedOrderId.Value)
                continue;

            var orderCapacityUnits = await ResolveOrderCapacityUnitsAsync(order, materialCache, ct);
            weeklyUsed += orderCapacityUnits;
            if (order.RequestedDeliveryDate == date)
                dailyUsed += orderCapacityUnits;
        }

        return new CapacityUsage(dailyUsed, weeklyUsed);
    }

    private async Task<decimal> ResolveOrderCapacityUnitsAsync(
        OrderRecord order,
        Dictionary<Material, MaterialSchedulingConfig> materialCache,
        CancellationToken ct)
    {
        if (order.CalculatedCapacityUnits.HasValue)
            return order.CalculatedCapacityUnits.Value;

        if (!materialCache.TryGetValue(order.Material, out var config))
        {
            config = await _materialConfigs.GetAsync(order.Material, ct);
            ValidateMaterialConfig(config);
            materialCache[order.Material] = config;
        }

        return OrderWorkItem.AllTeeth(order.WorkItems).Length * config.CapacityUnitsPerTooth;
    }

    private async Task<DateOnly> FindRecommendedDateAsync(DateOnly minimumDate, decimal orderCapacityUnits, long? excludedOrderId, CancellationToken ct)
    {
        var searchLimit = minimumDate.AddDays(SelectableDeadlineSearchLimitDays);
        for (var current = minimumDate; current <= searchLimit; current = current.AddDays(1))
        {
            if (!await _availability.CanSelectDeadlineAsync(current, ct))
                continue;

            var status = await EvaluateDateAsync(current, minimumDate, orderCapacityUnits, excludedOrderId, ct);
            if (status.IsSelectable)
                return current;
        }

        throw new InvalidOperationException("No capacity-available deadline found within 60 calendar days; manual scheduling is required.");
    }

    private static string? GetCapacityReason(bool isDailyCapacityExceeded, bool isWeeklyCapacityExceeded)
    {
        if (isDailyCapacityExceeded && isWeeklyCapacityExceeded)
            return "Daily and weekly capacity exceeded";
        if (isDailyCapacityExceeded)
            return "Daily capacity exceeded";
        if (isWeeklyCapacityExceeded)
            return "Weekly capacity exceeded";
        return null;
    }

    private async Task<DateOnly> NextBusinessDateAsync(DateOnly start, CancellationToken ct)
    {
        for (var current = start; ; current = current.AddDays(1))
        {
            if (!await _availability.IsClosedAsync(current, ct))
                return current;
        }
    }
}
