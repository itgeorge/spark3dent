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

internal sealed class TestMaterialSchedulingConfigProvider : IMaterialSchedulingConfigProvider
{
    private readonly IReadOnlyDictionary<Material, MaterialSchedulingConfig> _configs;

    public TestMaterialSchedulingConfigProvider(IEnumerable<MaterialSchedulingConfig>? configs = null) =>
        _configs = (configs ?? DefaultConfigs()).ToDictionary(c => c.Material);

    public Task<MaterialSchedulingConfig> GetAsync(Material material, CancellationToken ct = default)
    {
        if (_configs.TryGetValue(material, out var config))
            return Task.FromResult(config);
        throw new InvalidOperationException($"Material scheduling config is missing for {material}.");
    }

    public Task<IReadOnlyList<MaterialSchedulingConfig>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MaterialSchedulingConfig>>(_configs.Values.OrderBy(c => c.SortOrder).ToArray());

    public static MaterialSchedulingConfig DefaultConfig(Material material) => material switch
    {
        Material.Pmma => new(material, "PMMA", 2, 1.0m, null, true, 10),
        Material.PmmaTelio => new(material, "PMMA Telio", 2, 1.0m, null, true, 20),
        Material.FullContourZirconia => new(material, "Full Contour Zirconia", 3, 1.0m, null, true, 30),
        Material.GlassCeramics => new(material, "Glass Ceramics / LiSi", 4, 1.0m, null, true, 40),
        Material.Pfm => new(material, "PFM", 4, 1.0m, 10, true, 50),
        Material.PfzLayeredZrCrown => new(material, "PFZ Layered Zr Crown", 4, 1.0m, 10, true, 60),
        _ => throw new ArgumentOutOfRangeException(nameof(material), material, null)
    };

    public static IReadOnlyList<MaterialSchedulingConfig> DefaultConfigs() =>
        Enum.GetValues<Material>().Select(DefaultConfig).ToArray();
}

internal sealed class TestSchedulingCapacityConfigProvider : ISchedulingCapacityConfigProvider
{
    private readonly IReadOnlyList<SchedulingCapacityConfig> _configs;

    public TestSchedulingCapacityConfigProvider(IEnumerable<SchedulingCapacityConfig>? configs = null) =>
        _configs = (configs ?? [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 100m, 500m)])
            .OrderBy(c => c.ActiveFromDate)
            .ThenBy(c => c.Id)
            .ToArray();

    public Task<SchedulingCapacityConfig> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var config = _configs.LastOrDefault(c => c.ActiveFromDate <= date);
        if (config == null)
            throw new InvalidOperationException($"Scheduling capacity config is missing for {date:yyyy-MM-dd}.");
        return Task.FromResult(config);
    }

    public Task<IReadOnlyList<SchedulingCapacityConfig>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SchedulingCapacityConfig>>(_configs);
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

    public Task<SchedulingLab> BootstrapLabAsync(LabBootstrapRequest request, bool reset, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SchedulingClinic> CreateClinicWithInitialMemberAsync(ClinicCreateRequest request, MemberCreateRequest initialMember, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SchedulingClinic> UpdateClinicAsync(string clinicCode, ClinicUpdateRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SchedulingClinic> SetClinicActiveAsync(string clinicCode, bool isActive, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SchedulingMember> CreateMemberAsync(OrganizationType organizationType, string organizationCode, MemberCreateRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SchedulingMember> UpdateMemberLabelAsync(OrganizationType organizationType, string organizationCode, string memberId, string label, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SchedulingMember> SetMemberActiveAsync(OrganizationType organizationType, string organizationCode, string memberId, bool isActive, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SchedulingMember> UpdateMemberSecretAsync(OrganizationType organizationType, string organizationCode, string memberId, string pinHash, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
}

internal sealed class InMemorySchedulingWriteTransaction : ISchedulingWriteTransaction
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IOrderRepository _orders;
    private Func<IOrderRepository, Task>? _beforeOperationAsync;

    public InMemorySchedulingWriteTransaction(IOrderRepository orders)
    {
        _orders = orders;
    }

    public void SetBeforeOperation(Func<IOrderRepository, Task>? beforeOperationAsync)
    {
        _beforeOperationAsync = beforeOperationAsync;
    }

    public async Task<T> ExecuteAsync<T>(Func<IOrderRepository, Task<T>> operation, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var beforeOperation = Interlocked.Exchange(ref _beforeOperationAsync, null);
            if (beforeOperation != null)
                await beforeOperation(_orders);
            return await operation(_orders);
        }
        finally
        {
            _gate.Release();
        }
    }
}

internal static class TestActors
{
    public static readonly AuthenticatedActor Demo = new(OrganizationType.Clinic, "DEMO", "Demo", "cred-1", "Cred 1", "fingerprint", "session-1");
    public static readonly AuthenticatedActor Other = new(OrganizationType.Clinic, "OTHER", "Other Clinic", "cred-other", "Other Cred", "other-fingerprint", "session-2");
    public static readonly AuthenticatedActor Lab = new(OrganizationType.Lab, "LAB", "Spark3Dent Lab", "lab-1", "Lab Member 1", "lab-fingerprint", "session-lab");
}
