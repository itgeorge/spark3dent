using System.Security.Cryptography;
using System.Text;
using Utilities;

namespace Orders;

public sealed record LoginResult(string CookieToken, AuthenticatedActor Actor, DateTimeOffset ExpiresAt);

public sealed class SchedulingAuthService
{
    private readonly ISchedulingConfigProvider _configProvider;
    private readonly ISchedulingIdentityRepository _identities;
    private readonly IAuthSessionRepository _sessions;
    private readonly PinHasher _pinHasher;
    private readonly IClock _clock;

    public SchedulingAuthService(
        ISchedulingConfigProvider configProvider,
        ISchedulingIdentityRepository identities,
        IAuthSessionRepository sessions,
        PinHasher pinHasher,
        IClock clock)
    {
        _configProvider = configProvider;
        _identities = identities;
        _sessions = sessions;
        _pinHasher = pinHasher;
        _clock = clock;
    }

    public async Task<LoginResult> LoginAsync(string organizationCode, string pin, string ip, string userAgent, CancellationToken ct = default)
    {
        var normalizedCode = OrganizationCodes.Normalize(organizationCode);
        var organization = await _identities.FindOrganizationByCodeAsync(normalizedCode, includeInactive: false, ct)
            ?? throw new InvalidOperationException("Invalid credentials.");
        var member = (await _identities.ListMembersAsync(organization.OrganizationType, organization.Code, includeInactive: false, ct))
            .FirstOrDefault(m => _pinHasher.Verify(pin, m.PinHash));
        if (member == null)
            throw new InvalidOperationException("Invalid credentials.");

        var token = GenerateToken();
        var now = _clock.UtcNow;
        var expires = CalculateSlidingExpiry(now, null);
        var absolute = CalculateAbsoluteExpiry(now);
        if (absolute.HasValue && expires > absolute.Value) expires = absolute.Value;

        var session = new AuthSession(
            Guid.NewGuid().ToString("N"),
            organization.OrganizationType,
            organization.Code,
            member.Id,
            HashToken(token),
            now,
            now,
            expires,
            absolute,
            null,
            ip,
            userAgent);

        await _sessions.AddSessionAsync(session, ct);
        return new LoginResult(token, ToActor(organization, member, session.Id), expires);
    }

    public async Task<AuthenticatedActor?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var session = await _sessions.FindSessionByTokenHashAsync(HashToken(token), ct);
        if (!IsUsableSession(session)) return null;

        var organization = await _identities.GetOrganizationAsync(session!.OrganizationType, session.OrganizationCode, includeInactive: false, ct);
        if (organization == null) return null;

        var member = await _identities.GetMemberAsync(session.OrganizationType, session.OrganizationCode, session.MemberId, includeInactive: false, ct);
        if (member == null) return null;

        await RefreshSlidingExpiryAsync(session, ct);
        return ToActor(organization, member, session.Id);
    }

    public Task LogoutAsync(string sessionId, CancellationToken ct = default) =>
        _sessions.RevokeSessionAsync(sessionId, _clock.UtcNow, ct);

    public Task RevokeOrganizationSessionsAsync(OrganizationType organizationType, string organizationCode, CancellationToken ct = default) =>
        _sessions.RevokeOrganizationSessionsAsync(organizationType, organizationCode, _clock.UtcNow, ct);

    public Task RevokeMemberSessionsAsync(OrganizationType organizationType, string organizationCode, string memberId, CancellationToken ct = default) =>
        _sessions.RevokeMemberSessionsAsync(organizationType, organizationCode, memberId, _clock.UtcNow, ct);

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private bool IsUsableSession(AuthSession? session)
    {
        if (session == null || session.RevokedAt.HasValue) return false;
        var now = _clock.UtcNow;
        if (session.ExpiresAt <= now) return false;
        if (session.AbsoluteExpiresAt.HasValue && session.AbsoluteExpiresAt.Value <= now) return false;
        return true;
    }

    private async Task RefreshSlidingExpiryAsync(AuthSession session, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var newExpires = CalculateSlidingExpiry(now, session.AbsoluteExpiresAt);
        await _sessions.RefreshSessionAsync(session.Id, now, newExpires, ct);
    }

    private DateTimeOffset CalculateSlidingExpiry(DateTimeOffset now, DateTimeOffset? absolute)
    {
        var expires = now.AddDays(_configProvider.Current.Options.SessionSlidingDays);
        return absolute.HasValue && expires > absolute.Value ? absolute.Value : expires;
    }

    private DateTimeOffset? CalculateAbsoluteExpiry(DateTimeOffset now) =>
        _configProvider.Current.Options.SessionAbsoluteDays is { } days ? now.AddDays(days) : null;

    private static AuthenticatedActor ToActor(SchedulingOrganization organization, SchedulingMember member, string sessionId) =>
        new(organization.OrganizationType, organization.Code, organization.DisplayName, member.Id, member.Label, sessionId);

    private static string GenerateToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
