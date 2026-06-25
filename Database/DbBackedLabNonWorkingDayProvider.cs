using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class DbBackedLabNonWorkingDayProvider : INonWorkingDayProvider
{
    private readonly INonWorkingDayProvider _baseProvider;
    private readonly Func<AppDbContext> _contextFactory;

    public DbBackedLabNonWorkingDayProvider(Func<AppDbContext> contextFactory)
        : this(new BulgariaHardcodedNonWorkingDayProvider(), contextFactory)
    {
    }

    internal DbBackedLabNonWorkingDayProvider(INonWorkingDayProvider baseProvider, Func<AppDbContext> contextFactory)
    {
        _baseProvider = baseProvider;
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default)
    {
        var result = new HashSet<DateOnly>(await _baseProvider.GetNonWorkingDaysAsync(year, ct));
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        await using var ctx = _contextFactory();
        var offdays = await ctx.SchedulingLabOffdays.AsNoTracking()
            .Where(x => x.StartDate <= yearEnd && yearStart <= x.EndDate)
            .Select(x => new { x.StartDate, x.EndDate })
            .ToListAsync(ct);

        foreach (var offday in offdays)
        {
            var start = offday.StartDate < yearStart ? yearStart : offday.StartDate;
            var end = offday.EndDate > yearEnd ? yearEnd : offday.EndDate;
            for (var d = start; d <= end; d = d.AddDays(1))
                result.Add(d);
        }

        return result;
    }
}
