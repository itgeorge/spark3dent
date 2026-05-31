using Orders;
using Utilities;

namespace Orders.Tests;

internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
    public DateTimeOffset UtcNow { get; }
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.Date);
}

internal sealed class MutableClock : IClock
{
    public MutableClock(DateTimeOffset utcNow) => UtcNow = utcNow;
    public DateTimeOffset UtcNow { get; set; }
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.Date);
}

internal sealed class TestSchedulingConfigProvider : ISchedulingConfigProvider
{
    private TestSchedulingConfigProvider(SchedulingConfigSnapshot current) => Current = current;

    public SchedulingConfigSnapshot Current { get; private set; }
    public Task<SchedulingConfigSnapshot> ReloadAsync(CancellationToken ct = default) => Task.FromResult(Current);

    public static TestSchedulingConfigProvider Create(string? credentialHash = null) => new(new SchedulingConfigSnapshot(new SchedulingOptions
    {
        SessionSlidingDays = 30,
        SessionAbsoluteDays = 180,
        DefaultMinBusinessDays = 3,
        WorkRules =
        [
            new WorkRule(ProductCategory.Permanent, WorkType.Crown, Material.FullContourZirconia, ConstructionType.Crown, 3)
        ],
        Clinics =
        [
            new ClinicConfig
            {
                Code = "DEMO",
                DisplayName = "Demo",
                Credentials = credentialHash == null
                    ? []
                    : [new ClinicCredentialConfig { Id = "cred-1", Label = "Cred 1", PinHash = credentialHash, IsActive = true }]
            }
        ]
    }, DateTimeOffset.UtcNow, "test"));
}

internal static class TestActors
{
    public static readonly AuthenticatedActor Demo = new("DEMO", "Demo", "cred-1", "Cred 1", "fingerprint", "session-1");
}
