using System.Text.Json;
using System.Text.Json.Serialization;

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
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

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
        var basics = await FindLeadTimeRecommendationBasicsAsync(input, ct);
        return new DeadlineRecommendationResult(
            basics.EffectiveIntakeBusinessDate,
            basics.LeadTimeBusinessDays,
            basics.PostLeadTimeCandidateDate,
            basics.MinimumDeadlineDateFromLeadTime);
    }

    public async Task<DateOnly> RecommendCapacityAwareDateAsync(OrderSchedulingInput input, CancellationToken ct = default) =>
        await RecommendCapacityAwareDateAsync(input, orderRepositoryOverride: null, ct: ct);

    public async Task<DateOnly> RecommendCapacityAwareDateAsync(OrderSchedulingInput input, IOrderRepository? orderRepositoryOverride, CancellationToken ct = default)
    {
        return (await FindRecommendedDateWithDateEffectiveMaterialTrailAsync(input, orderRepositoryOverride, ct)).RecommendedDate
            ?? throw new InvalidOperationException("No capacity-available deadline found within 60 calendar days; manual scheduling is required.");
    }

    public async Task<DeadlineDateStatusesResult> GetCapacityAwareDateStatusesAsync(
        OrderSchedulingInput input,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default) =>
        await GetCapacityAwareDateStatusesAsync(input, start, end, orderRepositoryOverride: null, ct: ct);

    public async Task<DeadlineDateStatusesResult> GetCapacityAwareDateStatusesAsync(
        OrderSchedulingInput input,
        DateOnly start,
        DateOnly end,
        IOrderRepository? orderRepositoryOverride,
        CancellationToken ct = default)
    {
        if (end < start)
            throw new InvalidOperationException("End date must be on or after start date.");

        var startBasics = await CalculateRecommendationBasicsAsync(input, start, ct);
        var statuses = new List<DeliveryDateStatus>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var basics = await CalculateRecommendationBasicsAsync(input, date, ct);
            statuses.Add(await EvaluateDateAsync(date, basics.MinimumDeadlineDateFromLeadTime, basics.CalculatedOrderCapacityUnits, input.ExcludedOrderId, orderRepositoryOverride, ct));
        }

        DateOnly? recommendedDate = null;
        try
        {
            recommendedDate = (await FindRecommendedDateWithDateEffectiveMaterialTrailAsync(input, orderRepositoryOverride, ct)).RecommendedDate;
        }
        catch (InvalidOperationException)
        {
            // Statuses are still useful even when no recommendation exists inside the search window.
        }

        return new DeadlineDateStatusesResult(startBasics.MinimumDeadlineDateFromLeadTime, recommendedDate, startBasics.CalculatedOrderCapacityUnits, statuses);
    }

    public async Task<DeadlineValidationResult> ValidateRequestedDateAsync(
        OrderSchedulingInput input,
        DateOnly requestedDate,
        CancellationToken ct = default) =>
        await ValidateRequestedDateAsync(input, requestedDate, orderRepositoryOverride: null, ct: ct);

    public async Task<DeadlineValidationResult> ValidateRequestedDateAsync(
        OrderSchedulingInput input,
        DateOnly requestedDate,
        IOrderRepository? orderRepositoryOverride,
        CancellationToken ct = default) =>
        (await ValidateRequestedDateWithAuditAsync(input, requestedDate, orderRepositoryOverride, ct)).Validation;

    public async Task<DeadlineValidationWithAuditResult> ValidateRequestedDateWithAuditAsync(
        OrderSchedulingInput input,
        DateOnly requestedDate,
        IOrderRepository? orderRepositoryOverride,
        CancellationToken ct = default)
    {
        var basics = await CalculateRecommendationBasicsAsync(input, requestedDate, ct);
        var status = await EvaluateDateAsync(requestedDate, basics.MinimumDeadlineDateFromLeadTime, basics.CalculatedOrderCapacityUnits, input.ExcludedOrderId, orderRepositoryOverride, ct);

        DateOnly? recommendedDate = null;
        string? searchFailureReason = null;
        RecommendationSearchResult search;
        var failedRules = status.GetFailedRules().ToList();
        try
        {
            search = await FindRecommendedDateWithDateEffectiveMaterialTrailAsync(input, orderRepositoryOverride, ct);
            recommendedDate = search.RecommendedDate;
        }
        catch (InvalidOperationException ex)
        {
            searchFailureReason = ex.Message;
            failedRules.Add(DeadlineValidationRule.SearchFailure);
            search = new RecommendationSearchResult(
                null,
                basics.MinimumDeadlineDateFromLeadTime,
                basics.MinimumDeadlineDateFromLeadTime.AddDays(SelectableDeadlineSearchLimitDays),
                basics.MinimumDeadlineDateFromLeadTime.AddDays(SelectableDeadlineSearchLimitDays),
                []);
        }

        var validation = new DeadlineValidationResult(
            basics.MinimumDeadlineDateFromLeadTime,
            recommendedDate,
            status,
            basics.CalculatedOrderCapacityUnits,
            failedRules);
        var audit = BuildAudit(input, requestedDate, basics, search, searchFailureReason);
        return new DeadlineValidationWithAuditResult(validation, audit);
    }

    public async Task<decimal> CalculateCapacityUnitsAsync(Material material, IReadOnlyList<OrderWorkItem> workItems, DateOnly deadlineDate, CancellationToken ct = default)
    {
        OrderWorkItem.ValidateAll(workItems);
        var config = await _materialConfigs.GetForDateAsync(material, deadlineDate, ct);
        ValidateMaterialConfig(config);
        return OrderWorkItem.AllTeeth(workItems).Length * config.CapacityUnitsPerTooth;
    }

    public async Task<int> CalculateLeadTimeBusinessDaysAsync(Material material, IReadOnlyList<OrderWorkItem> workItems, DateOnly deadlineDate, CancellationToken ct = default)
    {
        var config = await _materialConfigs.GetForDateAsync(material, deadlineDate, ct);
        ValidateMaterialConfig(config);
        if (!UsesToothCountExtraLeadTime(material))
            return config.FixedLeadTimeBusinessDays;

        var teethPerExtraLeadDay = config.TeethPerExtraLeadDay!.Value;
        var distinctToothCount = OrderWorkItem.AllTeeth(workItems).Length;
        var extraLeadDays = (distinctToothCount + teethPerExtraLeadDay - 1) / teethPerExtraLeadDay;
        return config.FixedLeadTimeBusinessDays + extraLeadDays;
    }

    public async Task<IReadOnlyDictionary<DateOnly, DailyCapacityUsage>> GetDailyCapacityUsageByDateAsync(DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (end < start)
            throw new InvalidOperationException("End date must be on or after start date.");

        var orders = await _orders.ListActiveOrdersByDeadlineRangeAsync(start, end, ct);
        var materialCache = new Dictionary<(Material Material, DateOnly DeadlineDate), MaterialSchedulingConfig>();
        var usedByDate = new Dictionary<DateOnly, decimal>();
        foreach (var order in orders)
        {
            var orderCapacityUnits = await ResolveOrderCapacityUnitsAsync(order, materialCache, ct);
            usedByDate[order.RequestedDeliveryDate] = usedByDate.GetValueOrDefault(order.RequestedDeliveryDate) + orderCapacityUnits;
        }

        var result = new Dictionary<DateOnly, DailyCapacityUsage>();
        foreach (var (date, used) in usedByDate)
        {
            var capacityConfig = await _capacityConfigs.GetForDateAsync(date, ct);
            ValidateCapacityConfig(capacityConfig);
            result[date] = new DailyCapacityUsage(date, used, capacityConfig.DailyCapacityUnits);
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<DateOnly, WeeklyCapacityUsage>> GetWeeklyCapacityUsageByWeekEndAsync(DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (end < start)
            throw new InvalidOperationException("End date must be on or after start date.");

        var (expandedStart, _) = SchedulingWeek.GetRange(start);
        var (_, expandedEnd) = SchedulingWeek.GetRange(end);
        var orders = await _orders.ListActiveOrdersByDeadlineRangeAsync(expandedStart, expandedEnd, ct);
        var materialCache = new Dictionary<(Material Material, DateOnly DeadlineDate), MaterialSchedulingConfig>();
        var usedByWeekEnd = new Dictionary<DateOnly, decimal>();
        foreach (var order in orders)
        {
            var (_, weekEnd) = SchedulingWeek.GetRange(order.RequestedDeliveryDate);
            var orderCapacityUnits = await ResolveOrderCapacityUnitsAsync(order, materialCache, ct);
            usedByWeekEnd[weekEnd] = usedByWeekEnd.GetValueOrDefault(weekEnd) + orderCapacityUnits;
        }

        var result = new Dictionary<DateOnly, WeeklyCapacityUsage>();
        foreach (var (weekEnd, used) in usedByWeekEnd)
        {
            if (weekEnd < start || weekEnd > end)
                continue;
            var capacityConfig = await _capacityConfigs.GetForDateAsync(weekEnd, ct);
            ValidateCapacityConfig(capacityConfig);
            var weeklyCapacityLimit = await GetEffectiveWeeklyCapacityUnitsAsync(capacityConfig, weekEnd, ct);
            result[weekEnd] = new WeeklyCapacityUsage(weekEnd, used, weeklyCapacityLimit);
        }

        return result;
    }

    private static bool UsesToothCountExtraLeadTime(Material material) =>
        material is Material.Pfm or Material.PfzLayeredZrCrown;

    private static void ValidateMaterialConfig(MaterialSchedulingConfig config)
    {
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
        IOrderRepository? orderRepositoryOverride,
        CancellationToken ct)
    {
        var baseStatus = await _availability.GetStatusAsync(date, minimumDate, ct);
        if (baseStatus.IsClosed || baseStatus.IsFirstBusinessDayAfterClosure)
            return baseStatus with { OrderCapacityUnits = orderCapacityUnits };

        var capacityConfig = await _capacityConfigs.GetForDateAsync(date, ct);
        ValidateCapacityConfig(capacityConfig);
        var usage = await GetCapacityUsageAsync(date, excludedOrderId, orderRepositoryOverride, ct);
        var weeklyCapacityLimit = await GetEffectiveWeeklyCapacityUnitsAsync(capacityConfig, date, ct);
        var isDailyCapacityExceeded = usage.DailyUsed > 0m && usage.DailyUsed + orderCapacityUnits > capacityConfig.DailyCapacityUnits;
        var isWeeklyCapacityExceeded = usage.WeeklyUsed + orderCapacityUnits > weeklyCapacityLimit;
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
            weeklyCapacityLimit);
    }

    private async Task<decimal> GetEffectiveWeeklyCapacityUnitsAsync(SchedulingCapacityConfig capacityConfig, DateOnly date, CancellationToken ct)
    {
        var (weekStart, _) = SchedulingWeek.GetRange(date);
        var openWeekdays = 0;
        for (var offset = 0; offset < 5; offset++)
        {
            var weekday = weekStart.AddDays(offset);
            if (!await _availability.IsClosedAsync(weekday, ct))
                openWeekdays++;
        }

        return capacityConfig.WeeklyCapacityUnits * openWeekdays / 5m;
    }

    private async Task<CapacityUsage> GetCapacityUsageAsync(DateOnly date, long? excludedOrderId, IOrderRepository? orderRepositoryOverride, CancellationToken ct)
    {
        var (weekStart, weekEnd) = SchedulingWeek.GetRange(date);
        var orderRepository = orderRepositoryOverride ?? _orders;
        var orders = await orderRepository.ListActiveOrdersByDeadlineRangeAsync(weekStart, weekEnd, ct);
        var materialCache = new Dictionary<(Material Material, DateOnly DeadlineDate), MaterialSchedulingConfig>();
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
        Dictionary<(Material Material, DateOnly DeadlineDate), MaterialSchedulingConfig> materialCache,
        CancellationToken ct)
    {
        if (order.CalculatedCapacityUnits.HasValue)
            return order.CalculatedCapacityUnits.Value;

        var cacheKey = (order.Material, order.RequestedDeliveryDate);
        if (!materialCache.TryGetValue(cacheKey, out var config))
        {
            config = await _materialConfigs.GetForDateAsync(order.Material, order.RequestedDeliveryDate, ct);
            ValidateMaterialConfig(config);
            materialCache[cacheKey] = config;
        }

        return OrderWorkItem.AllTeeth(order.WorkItems).Length * config.CapacityUnitsPerTooth;
    }

    private async Task<DateOnly> FindRecommendedDateAsync(DateOnly minimumDate, decimal orderCapacityUnits, long? excludedOrderId, IOrderRepository? orderRepositoryOverride, CancellationToken ct) =>
        (await FindRecommendedDateWithTrailAsync(minimumDate, orderCapacityUnits, excludedOrderId, orderRepositoryOverride, ct)).RecommendedDate
        ?? throw new InvalidOperationException("No capacity-available deadline found within 60 calendar days; manual scheduling is required.");

    private async Task<RecommendationSearchResult> FindRecommendedDateWithTrailAsync(
        DateOnly minimumDate,
        decimal orderCapacityUnits,
        long? excludedOrderId,
        IOrderRepository? orderRepositoryOverride,
        CancellationToken ct)
    {
        var checks = new List<DeadlineRecommendationCandidateCheck>();
        var searchLimit = minimumDate.AddDays(SelectableDeadlineSearchLimitDays);
        for (var current = minimumDate; current <= searchLimit; current = current.AddDays(1))
        {
            var status = await EvaluateDateAsync(current, minimumDate, orderCapacityUnits, excludedOrderId, orderRepositoryOverride, ct);
            var accepted = status.IsSelectable;
            checks.Add(ToCandidateCheck(status, accepted));
            if (accepted)
                return new RecommendationSearchResult(current, minimumDate, current, searchLimit, checks);
        }

        throw new InvalidOperationException("No capacity-available deadline found within 60 calendar days; manual scheduling is required.");
    }

    private async Task<RecommendationSearchResult> FindRecommendedDateWithDateEffectiveMaterialTrailAsync(
        OrderSchedulingInput input,
        IOrderRepository? orderRepositoryOverride,
        CancellationToken ct)
    {
        var effectiveIntakeDate = await ResolveEffectiveIntakeBusinessDateAsync(input.ImpressionTimestampUtc, ct);
        var searchLimit = effectiveIntakeDate.AddDays(SelectableDeadlineSearchLimitDays);
        var checks = new List<DeadlineRecommendationCandidateCheck>();
        for (var current = effectiveIntakeDate; current <= searchLimit; current = current.AddDays(1))
        {
            var basics = await CalculateRecommendationBasicsAsync(input, current, ct);
            var status = await EvaluateDateAsync(current, basics.MinimumDeadlineDateFromLeadTime, basics.CalculatedOrderCapacityUnits, input.ExcludedOrderId, orderRepositoryOverride, ct);
            var accepted = status.IsSelectable;
            checks.Add(ToCandidateCheck(status, accepted));
            if (accepted)
                return new RecommendationSearchResult(current, effectiveIntakeDate, current, searchLimit, checks);
        }

        throw new InvalidOperationException("No capacity-available deadline found within 60 calendar days; manual scheduling is required.");
    }

    private async Task<RecommendationBasics> FindLeadTimeRecommendationBasicsAsync(OrderSchedulingInput input, CancellationToken ct)
    {
        var effectiveIntakeDate = await ResolveEffectiveIntakeBusinessDateAsync(input.ImpressionTimestampUtc, ct);
        var searchLimit = effectiveIntakeDate.AddDays(SelectableDeadlineSearchLimitDays);
        for (var current = effectiveIntakeDate; current <= searchLimit; current = current.AddDays(1))
        {
            var basics = await CalculateRecommendationBasicsAsync(input, current, ct);
            var status = await _availability.GetStatusAsync(current, basics.MinimumDeadlineDateFromLeadTime, ct);
            if (status.IsSelectable)
                return basics;
        }

        throw new InvalidOperationException("No selectable deadline found within 60 calendar days.");
    }

    private async Task<RecommendationBasics> CalculateRecommendationBasicsAsync(OrderSchedulingInput input, DateOnly deadlineDate, CancellationToken ct)
    {
        OrderWorkItem.ValidateAll(input.WorkItems);

        var materialConfig = await _materialConfigs.GetForDateAsync(input.Material, deadlineDate, ct);
        ValidateMaterialConfig(materialConfig);
        var toothCount = OrderWorkItem.AllTeeth(input.WorkItems).Length;
        var extraLeadDays = 0;
        if (UsesToothCountExtraLeadTime(input.Material))
        {
            var teethPerExtraLeadDay = materialConfig.TeethPerExtraLeadDay!.Value;
            extraLeadDays = (toothCount + teethPerExtraLeadDay - 1) / teethPerExtraLeadDay;
        }

        var leadTimeDays = materialConfig.FixedLeadTimeBusinessDays + extraLeadDays;
        var effectiveIntakeDate = await ResolveEffectiveIntakeBusinessDateAsync(input.ImpressionTimestampUtc, ct);
        var postLeadTimeCandidate = await CalculatePostLeadTimeCandidateAsync(effectiveIntakeDate, leadTimeDays, ct);
        var minimumDeadline = await AdvanceToSelectableDeadlineAsync(postLeadTimeCandidate, ct);
        var orderCapacityUnits = toothCount * materialConfig.CapacityUnitsPerTooth;

        return new RecommendationBasics(
            materialConfig,
            toothCount,
            effectiveIntakeDate,
            leadTimeDays,
            extraLeadDays,
            postLeadTimeCandidate,
            minimumDeadline,
            orderCapacityUnits);
    }

    private static DeadlineRecommendationAudit BuildAudit(
        OrderSchedulingInput input,
        DateOnly requestedDate,
        RecommendationBasics basics,
        RecommendationSearchResult search,
        string? searchFailureReason)
    {
        var configSnapshot = new
        {
            cutoffTimeUsed = IntakeCutoff.ToString("HH:mm"),
            materialConfig = new
            {
                material = basics.MaterialConfig.Material,
                basics.MaterialConfig.FixedLeadTimeBusinessDays,
                basics.MaterialConfig.TeethPerExtraLeadDay,
                basics.MaterialConfig.CapacityUnitsPerTooth,
                basics.MaterialConfig.ActiveFromDate
            },
            toothCount = basics.ToothCount,
            calculatedOrderCapacityUnits = basics.CalculatedOrderCapacityUnits,
            capacityChecks = search.CandidateChecks.Select(c => new
            {
                c.CandidateDate,
                dailyCapacityLimitUsed = c.DailyCapacityLimitUsed,
                weeklyCapacityLimitUsed = c.WeeklyCapacityLimitUsed,
                c.ExistingDailyCapacityUsed,
                c.ExistingWeeklyCapacityUsed,
                c.Accepted
            })
        };

        return new DeadlineRecommendationAudit(
            input.ImpressionTimestampUtc,
            basics.EffectiveIntakeBusinessDate,
            IntakeCutoff,
            input.Material,
            basics.ToothCount,
            basics.LeadTimeBusinessDays,
            basics.MaterialConfig.FixedLeadTimeBusinessDays,
            basics.ExtraLeadTimeBusinessDays,
            UsesToothCountExtraLeadTime(input.Material) ? basics.MaterialConfig.TeethPerExtraLeadDay : null,
            basics.MaterialConfig.CapacityUnitsPerTooth,
            basics.CalculatedOrderCapacityUnits,
            basics.MinimumDeadlineDateFromLeadTime,
            search.RecommendedDate,
            requestedDate,
            search.StartedAtDate,
            search.EndedAtDate,
            search.SearchLimitDate,
            search.RecommendedDate.HasValue ? "Accepted" : "Failure",
            searchFailureReason,
            search.CandidateChecks,
            JsonSerializer.Serialize(configSnapshot, AuditJsonOptions));
    }

    private static DeadlineRecommendationCandidateCheck ToCandidateCheck(DeliveryDateStatus status, bool accepted)
    {
        var rejectionReasons = status.GetFailedRules().Select(r => r.ToString()).ToList();
        if (!accepted && rejectionReasons.Count == 0 && !string.IsNullOrWhiteSpace(status.Reason))
            rejectionReasons.Add(status.Reason);
        var isCalendarSelectableDeadline = !status.IsClosed && !status.IsFirstBusinessDayAfterClosure;

        return new DeadlineRecommendationCandidateCheck(
            status.Date,
            isCalendarSelectableDeadline,
            status.IsClosed || status.IsFirstBusinessDayAfterClosure || status.IsBeforeMinimum ? status.Reason : null,
            status.DailyCapacityLimit,
            status.WeeklyCapacityLimit,
            status.ExistingDailyCapacityUsed,
            status.ExistingWeeklyCapacityUsed,
            status.OrderCapacityUnits ?? 0m,
            !status.IsDailyCapacityExceeded,
            !status.IsWeeklyCapacityExceeded,
            accepted,
            rejectionReasons);
    }

    private sealed record RecommendationBasics(
        MaterialSchedulingConfig MaterialConfig,
        int ToothCount,
        DateOnly EffectiveIntakeBusinessDate,
        int LeadTimeBusinessDays,
        int ExtraLeadTimeBusinessDays,
        DateOnly PostLeadTimeCandidateDate,
        DateOnly MinimumDeadlineDateFromLeadTime,
        decimal CalculatedOrderCapacityUnits);

    private sealed record RecommendationSearchResult(
        DateOnly? RecommendedDate,
        DateOnly StartedAtDate,
        DateOnly EndedAtDate,
        DateOnly SearchLimitDate,
        IReadOnlyList<DeadlineRecommendationCandidateCheck> CandidateChecks);

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
