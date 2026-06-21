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
    private readonly MaterialLeadTimeConfigProvider _leadTimeConfig;

    public DeadlineRecommendationService(DateAvailabilityService availability, MaterialLeadTimeConfigProvider leadTimeConfig)
    {
        _availability = availability;
        _leadTimeConfig = leadTimeConfig;
    }

    public async Task<DeadlineRecommendationResult> RecommendAsync(OrderSchedulingInput input, CancellationToken ct = default)
    {
        OrderWorkItem.ValidateAll(input.WorkItems);

        var effectiveIntakeDate = await ResolveEffectiveIntakeBusinessDateAsync(input.ImpressionTimestampUtc, ct);
        var leadTimeDays = CalculateLeadTimeBusinessDays(input.Material, input.WorkItems);
        var postLeadTimeCandidate = await CalculatePostLeadTimeCandidateAsync(effectiveIntakeDate, leadTimeDays, ct);
        var earliestSelectable = await AdvanceToSelectableDeadlineAsync(postLeadTimeCandidate, ct);

        return new DeadlineRecommendationResult(effectiveIntakeDate, leadTimeDays, postLeadTimeCandidate, earliestSelectable);
    }

    public int CalculateLeadTimeBusinessDays(Material material, IReadOnlyList<OrderWorkItem> workItems)
    {
        var config = _leadTimeConfig.Get(material);
        if (!config.UsesToothCountExtraLeadTime)
            return config.FixedLeadTimeBusinessDays;

        var distinctToothCount = OrderWorkItem.AllTeeth(workItems).Length;
        var extraLeadDays = (distinctToothCount + MaterialLeadTimeConfigProvider.TeethPerExtraLeadDay - 1)
            / MaterialLeadTimeConfigProvider.TeethPerExtraLeadDay;
        return config.FixedLeadTimeBusinessDays + extraLeadDays;
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
