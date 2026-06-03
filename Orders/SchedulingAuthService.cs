using System.Security.Cryptography;
using System.Text;
using Utilities;

namespace Orders;

public sealed record LoginResult(string CookieToken, AuthenticatedActor Actor, DateTimeOffset ExpiresAt);

public sealed class SchedulingAuthService
{
    private readonly ISchedulingConfigProvider _configProvider;
    private readonly IAuthSessionRepository _sessions;
    private readonly PinHasher _pinHasher;
    private readonly IClock _clock;

    public SchedulingAuthService(ISchedulingConfigProvider configProvider, IAuthSessionRepository sessions, PinHasher pinHasher, IClock clock)
    {
        _configProvider = configProvider;
        _sessions = sessions;
        _pinHasher = pinHasher;
        _clock = clock;
    }

    public async Task<LoginResult> LoginAsync(string clinicCode, string pin, string ip, string userAgent, CancellationToken ct = default)
    {
        var clinic = TryGetActiveClinic(clinicCode.Trim())
            ?? throw new InvalidOperationException("Invalid credentials.");
        var credential = FindMatchingCredential(clinic, pin);
        if (credential == null)
            throw new InvalidOperationException("Invalid credentials.");

        var token = GenerateToken();
        var now = _clock.UtcNow;
        var expires = CalculateSlidingExpiry(now, null);
        var absolute = CalculateAbsoluteExpiry(now);
        if (absolute.HasValue && expires > absolute.Value) expires = absolute.Value;

        var session = new AuthSession(
            Guid.NewGuid().ToString("N"),
            clinic.Code,
            credential.Id,
            HashToken(token),
            now,
            now,
            expires,
            absolute,
            null,
            ip,
            userAgent);

        await _sessions.AddSessionAsync(session, ct);
        return new LoginResult(token, ToActor(clinic, credential, session.Id), expires);
    }

    public async Task<AuthenticatedActor?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var session = await _sessions.FindSessionByTokenHashAsync(HashToken(token), ct);
        if (!IsUsableSession(session)) return null;

        var clinic = TryGetActiveClinic(session!.ClinicCode);
        if (clinic == null) return null;

        var credential = clinic.Credentials.FirstOrDefault(c => c.Id == session.CredentialId && c.IsActive);
        if (credential == null) return null;

        await RefreshSlidingExpiryAsync(session, ct);
        return ToActor(clinic, credential, session.Id);
    }

    public Task LogoutAsync(string sessionId, CancellationToken ct = default) =>
        _sessions.RevokeSessionAsync(sessionId, _clock.UtcNow, ct);

    public Task RevokeClinicSessionsAsync(string clinicCode, CancellationToken ct = default) =>
        _sessions.RevokeClinicSessionsAsync(clinicCode, _clock.UtcNow, ct);

    public Task RevokeCredentialSessionsAsync(string clinicCode, string credentialId, CancellationToken ct = default) =>
        _sessions.RevokeCredentialSessionsAsync(clinicCode, credentialId, _clock.UtcNow, ct);

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private ClinicCredentialConfig? FindMatchingCredential(ClinicConfig clinic, string pin) =>
        clinic.Credentials.FirstOrDefault(c => c.IsActive && _pinHasher.Verify(pin, c.PinHash));

    private bool IsUsableSession(AuthSession? session)
    {
        if (session == null || session.RevokedAt.HasValue) return false;
        var now = _clock.UtcNow;
        if (session.ExpiresAt <= now) return false;
        if (session.AbsoluteExpiresAt.HasValue && session.AbsoluteExpiresAt.Value <= now) return false;
        return true;
    }

    private ClinicConfig? TryGetActiveClinic(string clinicCode)
    {
        try { return _configProvider.Current.GetClinic(clinicCode); }
        catch (InvalidOperationException) { return null; }
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

    private static AuthenticatedActor ToActor(ClinicConfig clinic, ClinicCredentialConfig credential, string sessionId) =>
        new(clinic.Code, clinic.DisplayName, credential.Id, credential.Label, PinHasher.Fingerprint(credential.PinHash), sessionId, credential.Role);

    private static string GenerateToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
