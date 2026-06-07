using Orders;

namespace Orders.Tests;

public class SchedulingAuthServiceTest
{
    [Test]
    public async Task LoginAsync_GivenLabMember_ReturnsLabActor()
    {
        var hasher = new PinHasher();
        var members = new[]
        {
            new SchedulingMember(OrganizationType.Lab, "LAB", "lab-1", "Lab Member 1", hasher.Hash("654321", iterations: 10_000), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        var identities = new InMemorySchedulingIdentityRepository(members: members);
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryAuthSessionRepository();
        var service = new SchedulingAuthService(TestSchedulingConfigProvider.Create(), identities, repo, hasher, clock);

        var result = await service.LoginAsync("LAB", "654321", "127.0.0.1", "test");

        Assert.That(result.Actor.OrganizationType, Is.EqualTo(OrganizationType.Lab));
        Assert.That(result.Actor.IsLab, Is.True);
    }

    [Test]
    public async Task LoginAsync_GivenClinicMember_ReturnsClinicActor()
    {
        var hasher = new PinHasher();
        var members = new[]
        {
            new SchedulingMember(OrganizationType.Clinic, "DEMO", "cred-1", "Cred 1", hasher.Hash("123456", iterations: 10_000), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        var identities = new InMemorySchedulingIdentityRepository(members: members);
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryAuthSessionRepository();
        var service = new SchedulingAuthService(TestSchedulingConfigProvider.Create(), identities, repo, hasher, clock);

        var result = await service.LoginAsync("DEMO", "123456", "127.0.0.1", "test");

        Assert.That(result.Actor.OrganizationType, Is.EqualTo(OrganizationType.Clinic));
        Assert.That(result.Actor.IsClinic, Is.True);
    }

    [Test]
    public async Task AuthenticateAsync_GivenValidSession_RefreshesSlidingExpiry()
    {
        var hasher = new PinHasher();
        var identities = new InMemorySchedulingIdentityRepository(members:
        [
            new SchedulingMember(OrganizationType.Clinic, "DEMO", "cred-1", "Cred 1", hasher.Hash("123456", iterations: 10_000), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ]);
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryAuthSessionRepository();
        var token = "test-token";
        await repo.AddSessionAsync(BuildSession(token, clock.UtcNow, expiresAt: clock.UtcNow.AddMinutes(5)));
        var service = new SchedulingAuthService(TestSchedulingConfigProvider.Create(), identities, repo, hasher, clock);

        var actor = await service.AuthenticateAsync(token);

        Assert.That(actor, Is.Not.Null);
        Assert.That(repo.RefreshedSession!.ExpiresAt, Is.EqualTo(clock.UtcNow.AddDays(30)));
    }

    [Test]
    public async Task AuthenticateAsync_GivenExpiredSession_ReturnsNullWithoutRefresh()
    {
        var hasher = new PinHasher();
        var identities = new InMemorySchedulingIdentityRepository(members:
        [
            new SchedulingMember(OrganizationType.Clinic, "DEMO", "cred-1", "Cred 1", hasher.Hash("123456", iterations: 10_000), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ]);
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryAuthSessionRepository();
        var token = "test-token";
        await repo.AddSessionAsync(BuildSession(token, clock.UtcNow, expiresAt: clock.UtcNow.AddMinutes(-1)));
        var service = new SchedulingAuthService(TestSchedulingConfigProvider.Create(), identities, repo, hasher, clock);

        var actor = await service.AuthenticateAsync(token);

        Assert.That(actor, Is.Null);
        Assert.That(repo.RefreshedSession, Is.Null);
    }

    private static AuthSession BuildSession(string token, DateTimeOffset now, DateTimeOffset expiresAt) => new(
        "session-1",
        OrganizationType.Clinic,
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
        public Task RevokeOrganizationSessionsAsync(OrganizationType organizationType, string organizationCode, DateTimeOffset revokedAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeMemberSessionsAsync(OrganizationType organizationType, string organizationCode, string memberId, DateTimeOffset revokedAt, CancellationToken ct = default) => Task.CompletedTask;
    }
}
