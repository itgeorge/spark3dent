namespace Orders;

public sealed record OrderSchedulingInput(
    Material Material,
    IReadOnlyList<OrderWorkItem> WorkItems,
    DateTimeOffset ImpressionTimestampUtc);

public sealed record DeadlineRecommendationResult(
    DateOnly EffectiveIntakeBusinessDate,
    int LeadTimeBusinessDays,
    DateOnly PostLeadTimeCandidateDate,
    DateOnly EarliestSelectableDeadline);

public sealed class DeadlineRecommendationService
{
    private static readonly TimeOnly IntakeCutoff = new(11, 0);
    private const int SelectableDeadlineSearchLimitDays = 60;

    private readonly DateAvailabilityService _availability;
    private readonly IMaterialSchedulingConfigProvider _materialConfigs;

    public DeadlineRecommendationService(DateAvailabilityService availability, IMaterialSchedulingConfigProvider materialConfigs)
    {
        _availability = availability;
        _materialConfigs = materialConfigs;
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

    public async Task<int> CalculateLeadTimeBusinessDaysAsync(Material material, IReadOnlyList<OrderWorkItem> workItems, CancellationToken ct = default)
    {
        var config = await _materialConfigs.GetAsync(material, ct);
        ValidateConfig(config);
        if (!UsesToothCountExtraLeadTime(material))
            return config.FixedLeadTimeBusinessDays;

        var teethPerExtraLeadDay = config.TeethPerExtraLeadDay!.Value;
        var distinctToothCount = OrderWorkItem.AllTeeth(workItems).Length;
        var extraLeadDays = (distinctToothCount + teethPerExtraLeadDay - 1) / teethPerExtraLeadDay;
        return config.FixedLeadTimeBusinessDays + extraLeadDays;
    }

    private static bool UsesToothCountExtraLeadTime(Material material) =>
        material is Material.Pfm or Material.PfzLayeredZrCrown;

    private static void ValidateConfig(MaterialSchedulingConfig config)
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

    private async Task<DateOnly> NextBusinessDateAsync(DateOnly start, CancellationToken ct)
    {
        for (var current = start; ; current = current.AddDays(1))
        {
            if (!await _availability.IsClosedAsync(current, ct))
                return current;
        }
    }
}
