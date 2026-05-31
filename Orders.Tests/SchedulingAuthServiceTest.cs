using Orders;

namespace Orders.Tests;

public class SchedulingAuthServiceTest
{
    [Test]
    public async Task AuthenticateAsync_GivenValidSession_RefreshesSlidingExpiry()
    {
        var hasher = new PinHasher();
        var credentialHash = hasher.Hash("123456", iterations: 10_000);
        var config = TestSchedulingConfigProvider.Create(credentialHash);
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryAuthSessionRepository();
        var token = "test-token";
        await repo.AddSessionAsync(BuildSession(token, clock.UtcNow, expiresAt: clock.UtcNow.AddMinutes(5)));
        var service = new SchedulingAuthService(config, repo, hasher, clock);

        var actor = await service.AuthenticateAsync(token);

        Assert.That(actor, Is.Not.Null);
        Assert.That(repo.RefreshedSession!.ExpiresAt, Is.EqualTo(clock.UtcNow.AddDays(30)));
    }

    [Test]
    public async Task AuthenticateAsync_GivenExpiredSession_ReturnsNullWithoutRefresh()
    {
        var hasher = new PinHasher();
        var credentialHash = hasher.Hash("123456", iterations: 10_000);
        var config = TestSchedulingConfigProvider.Create(credentialHash);
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryAuthSessionRepository();
        var token = "test-token";
        await repo.AddSessionAsync(BuildSession(token, clock.UtcNow, expiresAt: clock.UtcNow.AddMinutes(-1)));
        var service = new SchedulingAuthService(config, repo, hasher, clock);

        var actor = await service.AuthenticateAsync(token);

        Assert.That(actor, Is.Null);
        Assert.That(repo.RefreshedSession, Is.Null);
    }

    private static AuthSession BuildSession(string token, DateTimeOffset now, DateTimeOffset expiresAt) => new(
        "session-1",
        "DEMO",
        "cred-1",
        SchedulingAuthService.HashToken(token),
        now.AddDays(-1),
        now.AddDays(-1),
        expiresAt,
        now.AddDays(180),
        null,
        "127.0.0.1",
        "test");

    private sealed class InMemoryAuthSessionRepository : IAuthSessionRepository
    {
        private readonly Dictionary<string, AuthSession> _sessionsByTokenHash = new(StringComparer.OrdinalIgnoreCase);

        public AuthSession? RefreshedSession { get; private set; }

        public Task AddSessionAsync(AuthSession session, CancellationToken ct = default)
        {
            _sessionsByTokenHash[session.TokenHash] = session;
            return Task.CompletedTask;
        }

        public Task<AuthSession?> FindSessionByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(_sessionsByTokenHash.TryGetValue(tokenHash, out var session) ? session : null);

        public Task RefreshSessionAsync(string sessionId, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, CancellationToken ct = default)
        {
            var session = _sessionsByTokenHash.Values.Single(s => s.Id == sessionId);
            RefreshedSession = session with { LastSeenAt = lastSeenAt, ExpiresAt = expiresAt };
            _sessionsByTokenHash[session.TokenHash] = RefreshedSession;
            return Task.CompletedTask;
        }

        public Task RevokeSessionAsync(string sessionId, DateTimeOffset revokedAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeClinicSessionsAsync(string clinicCode, DateTimeOffset revokedAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeCredentialSessionsAsync(string clinicCode, string credentialId, DateTimeOffset revokedAt, CancellationToken ct = default) => Task.CompletedTask;
    }
}
