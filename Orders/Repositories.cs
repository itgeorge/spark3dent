namespace Orders;

public interface IAuthSessionRepository
{
    Task AddSessionAsync(AuthSession session, CancellationToken ct = default);
    Task<AuthSession?> FindSessionByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task RefreshSessionAsync(string sessionId, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task RevokeSessionAsync(string sessionId, DateTimeOffset revokedAt, CancellationToken ct = default);
    Task RevokeOrganizationSessionsAsync(OrganizationType organizationType, string organizationCode, DateTimeOffset revokedAt, CancellationToken ct = default);
    Task RevokeMemberSessionsAsync(OrganizationType organizationType, string organizationCode, string memberId, DateTimeOffset revokedAt, CancellationToken ct = default);
}

public interface ISchedulingIdentityRepository
{
    Task<SchedulingOrganization?> FindOrganizationByCodeAsync(string organizationCode, bool includeInactive = false, CancellationToken ct = default);
    Task<SchedulingOrganization?> GetOrganizationAsync(OrganizationType organizationType, string organizationCode, bool includeInactive = false, CancellationToken ct = default);
    Task<SchedulingLab?> GetLabAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<SchedulingClinic?> GetClinicAsync(string clinicCode, bool includeInactive = false, CancellationToken ct = default);
    Task<IReadOnlyList<SchedulingClinic>> ListClinicsAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<SchedulingMember?> GetMemberAsync(OrganizationType organizationType, string organizationCode, string memberId, bool includeInactive = false, CancellationToken ct = default);
    Task<IReadOnlyList<SchedulingMember>> ListMembersAsync(OrganizationType organizationType, string organizationCode, bool includeInactive = false, CancellationToken ct = default);

    Task<SchedulingLab> BootstrapLabAsync(LabBootstrapRequest request, bool reset, CancellationToken ct = default);
    Task<SchedulingClinic> CreateClinicWithInitialMemberAsync(ClinicCreateRequest request, MemberCreateRequest initialMember, CancellationToken ct = default);
    Task<SchedulingClinic> UpdateClinicAsync(string clinicCode, ClinicUpdateRequest request, CancellationToken ct = default);
    Task<SchedulingClinic> SetClinicActiveAsync(string clinicCode, bool isActive, DateTimeOffset now, CancellationToken ct = default);
    Task<SchedulingMember> CreateMemberAsync(OrganizationType organizationType, string organizationCode, MemberCreateRequest request, CancellationToken ct = default);
    Task<SchedulingMember> UpdateMemberLabelAsync(OrganizationType organizationType, string organizationCode, string memberId, string label, DateTimeOffset now, CancellationToken ct = default);
    Task<SchedulingMember> SetMemberActiveAsync(OrganizationType organizationType, string organizationCode, string memberId, bool isActive, DateTimeOffset now, CancellationToken ct = default);
    Task<SchedulingMember> UpdateMemberSecretAsync(OrganizationType organizationType, string organizationCode, string memberId, string pinHash, DateTimeOffset now, CancellationToken ct = default);
}

public sealed record LabBootstrapRequest(string LabCode, string LabDisplayName, string MemberId, string MemberLabel, string MemberPinHash, DateTimeOffset Now);
public sealed record ClinicCreateRequest(string Code, string DisplayName, string? LinkedClientNickname, string? DisplayColor, DateTimeOffset Now);
public sealed record ClinicUpdateRequest(string DisplayName, string? LinkedClientNickname, string? DisplayColor, DateTimeOffset Now);
public sealed record MemberCreateRequest(string Id, string Label, string PinHash, DateTimeOffset Now);

public interface IOrderRepository
{
    Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default);
    Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default);
    Task<OrderRecord> UpdateOrderAsync(OrderRecord order, CancellationToken ct = default);
    Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default);
    Task<OrderPage> ListOrdersPageAsync(string? clinicCode, int limit, OrderCursor? cursor, CancellationToken ct = default);
    Task<OrderPage> ListOrdersPageContainingOrderAsync(string? clinicCode, OrderRecord target, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<OrderRecord>> FindOrdersByCodeSuffixAsync(string? clinicCode, string codeSuffix, int limit = 2, CancellationToken ct = default);
    Task<IReadOnlyList<OrderRecord>> ListActiveOrdersForCalendarAsync(string? clinicCode, DateOnly start, DateOnly end, CancellationToken ct = default);
}

public sealed class DuplicateOrderCodeException : InvalidOperationException
{
    public DuplicateOrderCodeException(string orderCode, Exception? innerException = null)
        : base($"Duplicate order code: {orderCode}", innerException)
    {
        OrderCode = orderCode;
    }

    public string OrderCode { get; }
}
