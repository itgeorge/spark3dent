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

    public static TestSchedulingConfigProvider Create(List<WorkRule>? workRules = null) => new(new SchedulingConfigSnapshot(new SchedulingOptions
    {
        SessionSlidingDays = 30,
        SessionAbsoluteDays = 180,
        DefaultMinBusinessDays = 3,
        WorkRules = workRules ??
        [
            new WorkRule(ProductCategory.Permanent, WorkType.Crown, Material.FullContourZirconia, ConstructionType.Crown, 3)
        ]
    }, DateTimeOffset.UtcNow, "test"));
}

internal sealed class InMemorySchedulingIdentityRepository : ISchedulingIdentityRepository
{
    private readonly Dictionary<string, SchedulingLab> _labsByCode;
    private readonly Dictionary<string, SchedulingClinic> _clinicsByCode;
    private readonly List<SchedulingMember> _members;

    public InMemorySchedulingIdentityRepository(IEnumerable<SchedulingLab>? labs = null, IEnumerable<SchedulingClinic>? clinics = null, IEnumerable<SchedulingMember>? members = null)
    {
        _labsByCode = (labs ?? [DemoLab()]).ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        _clinicsByCode = (clinics ?? [DemoClinic(), OtherClinic()]).ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        _members = (members ?? []).ToList();
    }

    public static SchedulingLab DemoLab() => new(1, "LAB", "Spark3Dent Lab", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    public static SchedulingClinic DemoClinic() => new("DEMO", "Demo", null, null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    public static SchedulingClinic OtherClinic() => new("OTHER", "Other Clinic", null, null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public Task<SchedulingOrganization?> FindOrganizationByCodeAsync(string organizationCode, bool includeInactive = false, CancellationToken ct = default)
    {
        var code = organizationCode.Trim();
        if (_labsByCode.TryGetValue(code, out var lab) && (includeInactive || lab.IsActive))
            return Task.FromResult<SchedulingOrganization?>(new SchedulingOrganization(OrganizationType.Lab, lab.Code, lab.DisplayName, lab.IsActive));
        if (_clinicsByCode.TryGetValue(code, out var clinic) && (includeInactive || clinic.IsActive))
            return Task.FromResult<SchedulingOrganization?>(new SchedulingOrganization(OrganizationType.Clinic, clinic.Code, clinic.DisplayName, clinic.IsActive));
        return Task.FromResult<SchedulingOrganization?>(null);
    }

    public Task<SchedulingOrganization?> GetOrganizationAsync(OrganizationType organizationType, string organizationCode, bool includeInactive = false, CancellationToken ct = default)
    {
        var code = organizationCode.Trim();
        if (organizationType == OrganizationType.Lab)
        {
            if (_labsByCode.TryGetValue(code, out var lab) && (includeInactive || lab.IsActive))
                return Task.FromResult<SchedulingOrganization?>(new SchedulingOrganization(OrganizationType.Lab, lab.Code, lab.DisplayName, lab.IsActive));
            return Task.FromResult<SchedulingOrganization?>(null);
        }

        if (_clinicsByCode.TryGetValue(code, out var clinic) && (includeInactive || clinic.IsActive))
            return Task.FromResult<SchedulingOrganization?>(new SchedulingOrganization(OrganizationType.Clinic, clinic.Code, clinic.DisplayName, clinic.IsActive));
        return Task.FromResult<SchedulingOrganization?>(null);
    }

    public Task<SchedulingLab?> GetLabAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var lab = _labsByCode.Values.FirstOrDefault();
        return Task.FromResult(lab != null && (includeInactive || lab.IsActive) ? lab : null);
    }

    public Task<SchedulingClinic?> GetClinicAsync(string clinicCode, bool includeInactive = false, CancellationToken ct = default)
    {
        if (_clinicsByCode.TryGetValue(clinicCode.Trim(), out var clinic) && (includeInactive || clinic.IsActive))
            return Task.FromResult<SchedulingClinic?>(clinic);
        return Task.FromResult<SchedulingClinic?>(null);
    }

    public Task<IReadOnlyList<SchedulingClinic>> ListClinicsAsync(bool includeInactive = false, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SchedulingClinic>>(_clinicsByCode.Values.Where(x => includeInactive || x.IsActive).ToList());

    public Task<SchedulingMember?> GetMemberAsync(OrganizationType organizationType, string organizationCode, string memberId, bool includeInactive = false, CancellationToken ct = default)
    {
        var member = _members.FirstOrDefault(x =>
            x.OrganizationType == organizationType
            && string.Equals(x.OrganizationCode, organizationCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Id, memberId, StringComparison.OrdinalIgnoreCase)
            && (includeInactive || x.IsActive));
        return Task.FromResult(member);
    }

    public Task<IReadOnlyList<SchedulingMember>> ListMembersAsync(OrganizationType organizationType, string organizationCode, bool includeInactive = false, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SchedulingMember>>(_members
            .Where(x => x.OrganizationType == organizationType
                && string.Equals(x.OrganizationCode, organizationCode, StringComparison.OrdinalIgnoreCase)
                && (includeInactive || x.IsActive))
            .ToList());
}

internal static class TestActors
{
    public static readonly AuthenticatedActor Demo = new(OrganizationType.Clinic, "DEMO", "Demo", "cred-1", "Cred 1", "fingerprint", "session-1");
    public static readonly AuthenticatedActor Other = new(OrganizationType.Clinic, "OTHER", "Other Clinic", "cred-other", "Other Cred", "other-fingerprint", "session-2");
    public static readonly AuthenticatedActor Lab = new(OrganizationType.Lab, "LAB", "Spark3Dent Lab", "lab-1", "Lab Member 1", "lab-fingerprint", "session-lab");
}
