using Orders;
using Utilities;

namespace Orders.Tests;

public class OrderTechnicalHardeningTests
{
    [Test]
    public async Task CreateOrderAsync_WhenConcurrentRequestsRaceOnSameOrderCode_RetriesAndBothSucceed()
    {
        var repo = new RaceySchedulingRepository();
        var service = new SchedulingOrderService(
            TestConfigProvider.Create(),
            repo,
            new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider()),
            new SequenceOrderCodeGenerator("ABC-234", "ABC-234", "DEF-567"),
            new FixedClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero)));
        var actor = TestActors.Demo;
        var draft = new OrderDraft(
            "Race case",
            new DateOnly(2026, 6, 2),
            ProductCategory.Permanent,
            WorkType.Crown,
            Material.FullContourZirconia,
            ConstructionType.Crown,
            new ToothRange(11, 11),
            new DateOnly(2026, 6, 5),
            null);

        var results = await Task.WhenAll(
            service.CreateOrderAsync(actor, draft, "127.0.0.1", "test-a"),
            service.CreateOrderAsync(actor, draft with { CaseName = "Race case 2" }, "127.0.0.1", "test-b"));

        Assert.That(results.Select(r => r.OrderCode), Is.Unique);
        Assert.That(results.Select(r => r.OrderCode), Does.Contain("ABC-234"));
        Assert.That(results.Select(r => r.OrderCode), Does.Contain("DEF-567"));
    }

    [Test]
    public async Task AuthenticateAsync_GivenValidSession_RefreshesSlidingExpiry()
    {
        var hasher = new PinHasher();
        var credentialHash = hasher.Hash("123456", iterations: 10_000);
        var config = TestConfigProvider.Create(credentialHash);
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryAuthSessionRepository();
        var token = "test-token";
        await repo.AddSessionAsync(new AuthSession(
            "session-1",
            "DEMO",
            "cred-1",
            SchedulingAuthService.HashToken(token),
            clock.UtcNow.AddDays(-1),
            clock.UtcNow.AddDays(-1),
            clock.UtcNow.AddMinutes(5),
            clock.UtcNow.AddDays(180),
            null,
            "127.0.0.1",
            "test"));
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
        var config = TestConfigProvider.Create(credentialHash);
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        var repo = new InMemoryAuthSessionRepository();
        var token = "test-token";
        await repo.AddSessionAsync(new AuthSession(
            "session-1",
            "DEMO",
            "cred-1",
            SchedulingAuthService.HashToken(token),
            clock.UtcNow.AddDays(-2),
            clock.UtcNow.AddDays(-2),
            clock.UtcNow.AddMinutes(-1),
            clock.UtcNow.AddDays(180),
            null,
            "127.0.0.1",
            "test"));
        var service = new SchedulingAuthService(config, repo, hasher, clock);

        var actor = await service.AuthenticateAsync(token);

        Assert.That(actor, Is.Null);
        Assert.That(repo.RefreshedSession, Is.Null);
    }

    [Test]
    public async Task DateAvailabilityService_LoadsNonWorkingDaysOncePerYearForRange()
    {
        var provider = new CountingNonWorkingDayProvider();
        var service = new DateAvailabilityService(provider);
        await service.GetStatusesAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 10),
            new DateOnly(2026, 6, 1));
        Assert.That(provider.CallsByYear[2026], Is.EqualTo(1));
    }

    private sealed class SequenceOrderCodeGenerator : IOrderCodeGenerator
    {
        private readonly Queue<string> _codes;
        private readonly object _gate = new();

        public SequenceOrderCodeGenerator(params string[] codes) => _codes = new Queue<string>(codes);

        public string Generate()
        {
            lock (_gate)
            {
                if (_codes.Count == 0) throw new InvalidOperationException("No more test order codes.");
                return _codes.Dequeue();
            }
        }
    }

    private sealed class RaceySchedulingRepository : IOrderRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, OrderRecord> _ordersByCode = new(StringComparer.OrdinalIgnoreCase);
        private long _nextId = 1;

        public Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default)
        {
            lock (_gate)
            {
                if (_ordersByCode.ContainsKey(order.OrderCode))
                    throw new DuplicateOrderCodeException(order.OrderCode);
                var saved = order with { Id = _nextId++ };
                _ordersByCode.Add(saved.OrderCode, saved);
                return Task.FromResult(saved);
            }
        }

        public Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default)
        {
            lock (_gate)
                return Task.FromResult(_ordersByCode.TryGetValue(orderCode, out var order) ? order : null);
        }

        public Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default)
        {
            lock (_gate)
                return Task.FromResult<IReadOnlyList<OrderRecord>>(_ordersByCode.Values.ToList());
        }
    }

    private sealed class CountingNonWorkingDayProvider : INonWorkingDayProvider
    {
        public Dictionary<int, int> CallsByYear { get; } = new();

        public Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default)
        {
            CallsByYear[year] = CallsByYear.GetValueOrDefault(year) + 1;
            return new WeekendOnlyNonWorkingDayProvider().GetNonWorkingDaysAsync(year, ct);
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.Date);
    }

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.Date);
    }

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

    private sealed class TestConfigProvider : ISchedulingConfigProvider
    {
        private TestConfigProvider(SchedulingConfigSnapshot current) => Current = current;
        public SchedulingConfigSnapshot Current { get; private set; }
        public Task<SchedulingConfigSnapshot> ReloadAsync(CancellationToken ct = default) => Task.FromResult(Current);

        public static TestConfigProvider Create(string? credentialHash = null) => new(new SchedulingConfigSnapshot(new SchedulingOptions
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

    private static class TestActors
    {
        public static readonly AuthenticatedActor Demo = new("DEMO", "Demo", "cred-1", "Cred 1", "fingerprint", "session-1");
    }
}
