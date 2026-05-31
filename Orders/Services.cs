using System.Security.Cryptography;
using System.Text;

namespace Orders;

public interface INonWorkingDayProvider
{
    Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default);
}

public sealed class WeekendOnlyNonWorkingDayProvider : INonWorkingDayProvider
{
    public Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default)
    {
        var dates = new HashSet<DateOnly>();
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            if (DateAvailabilityService.IsWeekend(d)) dates.Add(d);
        }
        return Task.FromResult<IReadOnlySet<DateOnly>>(dates);
    }
}

public sealed record DeliveryDateStatus(DateOnly Date, bool IsClosed, bool IsFirstBusinessDayAfterClosure, bool IsBeforeMinimum, bool IsSelectable, string? Reason);

public sealed class DateAvailabilityService
{
    private readonly INonWorkingDayProvider _nonWorkingDayProvider;

    public DateAvailabilityService(INonWorkingDayProvider nonWorkingDayProvider)
    {
        _nonWorkingDayProvider = nonWorkingDayProvider;
    }

    public async Task<DateOnly> CalculateMinimumDateAsync(DateOnly impressionDate, int minBusinessDays, CancellationToken ct = default)
    {
        if (minBusinessDays < 0) throw new InvalidOperationException("Minimum business days must be non-negative.");
        var current = impressionDate;
        var counted = 0;
        while (counted < minBusinessDays)
        {
            current = current.AddDays(1);
            if (!await IsClosedAsync(current, ct)) counted++;
        }
        return current;
    }

    public async Task<DeliveryDateStatus> GetStatusAsync(DateOnly date, DateOnly minimumDate, CancellationToken ct = default)
    {
        var isClosed = await IsClosedAsync(date, ct);
        var isFirst = !isClosed && await IsClosedAsync(date.AddDays(-1), ct);
        var isBeforeMinimum = date < minimumDate;
        string? reason = null;
        if (isBeforeMinimum) reason = "Before minimum lead time";
        else if (isClosed) reason = IsWeekend(date) ? "Weekend" : "Closed/non-working day";
        else if (isFirst) reason = "First business day after weekend/closure";
        var selectable = reason == null;
        return new DeliveryDateStatus(date, isClosed, isFirst, isBeforeMinimum, selectable, reason);
    }

    public async Task<IReadOnlyList<DeliveryDateStatus>> GetStatusesAsync(DateOnly start, DateOnly end, DateOnly minimumDate, CancellationToken ct = default)
    {
        if (end < start) throw new InvalidOperationException("End date must be on or after start date.");
        var result = new List<DeliveryDateStatus>();
        for (var d = start; d <= end; d = d.AddDays(1))
            result.Add(await GetStatusAsync(d, minimumDate, ct));
        return result;
    }

    public async Task ValidateDeliveryDateAsync(DateOnly date, DateOnly minimumDate, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(date, minimumDate, ct);
        if (!status.IsSelectable)
            throw new InvalidOperationException($"Delivery date {date:yyyy-MM-dd} is not available: {status.Reason}.");
    }

    private async Task<bool> IsClosedAsync(DateOnly date, CancellationToken ct)
    {
        if (IsWeekend(date)) return true;
        var nonWorking = await _nonWorkingDayProvider.GetNonWorkingDaysAsync(date.Year, ct);
        return nonWorking.Contains(date);
    }

    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}

public interface IOrderCodeGenerator
{
    string Generate();
}

public sealed class SafeOrderCodeGenerator : IOrderCodeGenerator
{
    private const string Alphabet = "23456789ACDEFGHJKMNPQRSTWXYZ";

    public string Generate()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return $"{new string(chars, 0, 3)}-{new string(chars, 3, 3)}";
    }
}

public interface ISchedulingRepository
{
    Task AddSessionAsync(AuthSession session, CancellationToken ct = default);
    Task<AuthSession?> FindSessionByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task RefreshSessionAsync(string sessionId, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task RevokeSessionAsync(string sessionId, DateTimeOffset revokedAt, CancellationToken ct = default);
    Task RevokeClinicSessionsAsync(string clinicCode, DateTimeOffset revokedAt, CancellationToken ct = default);
    Task RevokeCredentialSessionsAsync(string clinicCode, string credentialId, DateTimeOffset revokedAt, CancellationToken ct = default);
    Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken ct = default);
    Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default);
    Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default);
    Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default);
}

public sealed record LoginResult(string CookieToken, AuthenticatedActor Actor, DateTimeOffset ExpiresAt);

public sealed class SchedulingAuthService
{
    private readonly ISchedulingConfigProvider _configProvider;
    private readonly ISchedulingRepository _repository;
    private readonly PinHasher _pinHasher;
    private readonly IClock _clock;

    public SchedulingAuthService(ISchedulingConfigProvider configProvider, ISchedulingRepository repository, PinHasher pinHasher, IClock clock)
    {
        _configProvider = configProvider;
        _repository = repository;
        _pinHasher = pinHasher;
        _clock = clock;
    }

    public async Task<LoginResult> LoginAsync(string clinicCode, string pin, string ip, string userAgent, CancellationToken ct = default)
    {
        PinHasher.ValidatePinShape(pin);
        var clinic = _configProvider.Current.GetClinic(clinicCode.Trim());
        var credential = clinic.Credentials.FirstOrDefault(c => c.IsActive && _pinHasher.Verify(pin, c.PinHash));
        if (credential == null)
            throw new InvalidOperationException("Invalid clinic code or PIN.");

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncode(tokenBytes);
        var tokenHash = HashToken(token);
        var now = _clock.UtcNow;
        var sliding = TimeSpan.FromDays(_configProvider.Current.Options.SessionSlidingDays);
        var absolute = _configProvider.Current.Options.SessionAbsoluteDays is { } days ? now.AddDays(days) : (DateTimeOffset?)null;
        var expires = now.Add(sliding);
        if (absolute.HasValue && expires > absolute.Value) expires = absolute.Value;
        var session = new AuthSession(Guid.NewGuid().ToString("N"), clinic.Code, credential.Id, tokenHash, now, now, expires, absolute, null, ip, userAgent);
        await _repository.AddSessionAsync(session, ct);
        return new LoginResult(token, ToActor(clinic, credential, session.Id), expires);
    }

    public async Task<AuthenticatedActor?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var tokenHash = HashToken(token);
        var session = await _repository.FindSessionByTokenHashAsync(tokenHash, ct);
        if (session == null || session.RevokedAt.HasValue) return null;
        var now = _clock.UtcNow;
        if (session.ExpiresAt <= now) return null;
        if (session.AbsoluteExpiresAt.HasValue && session.AbsoluteExpiresAt.Value <= now) return null;

        ClinicConfig clinic;
        try { clinic = _configProvider.Current.GetClinic(session.ClinicCode); }
        catch { return null; }
        var credential = clinic.Credentials.FirstOrDefault(c => c.Id == session.CredentialId && c.IsActive);
        if (credential == null) return null;

        var sliding = TimeSpan.FromDays(_configProvider.Current.Options.SessionSlidingDays);
        var newExpires = now.Add(sliding);
        if (session.AbsoluteExpiresAt.HasValue && newExpires > session.AbsoluteExpiresAt.Value) newExpires = session.AbsoluteExpiresAt.Value;
        await _repository.RefreshSessionAsync(session.Id, now, newExpires, ct);
        return ToActor(clinic, credential, session.Id);
    }

    public Task LogoutAsync(string sessionId, CancellationToken ct = default) =>
        _repository.RevokeSessionAsync(sessionId, _clock.UtcNow, ct);

    public Task RevokeClinicSessionsAsync(string clinicCode, CancellationToken ct = default) =>
        _repository.RevokeClinicSessionsAsync(clinicCode, _clock.UtcNow, ct);

    public Task RevokeCredentialSessionsAsync(string clinicCode, string credentialId, CancellationToken ct = default) =>
        _repository.RevokeCredentialSessionsAsync(clinicCode, credentialId, _clock.UtcNow, ct);

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static AuthenticatedActor ToActor(ClinicConfig clinic, ClinicCredentialConfig credential, string sessionId) =>
        new(clinic.Code, clinic.DisplayName, credential.Id, credential.Label, PinHasher.Fingerprint(credential.PinHash), sessionId);

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class SchedulingOrderService
{
    private readonly ISchedulingConfigProvider _configProvider;
    private readonly ISchedulingRepository _repository;
    private readonly DateAvailabilityService _availability;
    private readonly IOrderCodeGenerator _codeGenerator;
    private readonly IClock _clock;

    public SchedulingOrderService(ISchedulingConfigProvider configProvider, ISchedulingRepository repository, DateAvailabilityService availability, IOrderCodeGenerator codeGenerator, IClock clock)
    {
        _configProvider = configProvider;
        _repository = repository;
        _availability = availability;
        _codeGenerator = codeGenerator;
        _clock = clock;
    }

    public async Task<DateOnly> CalculateMinimumDeliveryDateAsync(OrderDraft draft, CancellationToken ct = default)
    {
        var rule = _configProvider.Current.FindWorkRule(draft.ProductCategory, draft.WorkType, draft.Material, draft.ConstructionType);
        return await _availability.CalculateMinimumDateAsync(draft.ImpressionDate, rule.MinBusinessDays, ct);
    }

    public async Task<IReadOnlyList<DeliveryDateStatus>> GetDateStatusesAsync(OrderDraft draft, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var minimum = await CalculateMinimumDeliveryDateAsync(draft, ct);
        return await _availability.GetStatusesAsync(start, end, minimum, ct);
    }

    public async Task<OrderRecord> CreateOrderAsync(AuthenticatedActor actor, OrderDraft draft, string ip, string userAgent, CancellationToken ct = default)
    {
        draft.TeethRange.Validate(draft.ConstructionType);
        var minimum = await CalculateMinimumDeliveryDateAsync(draft, ct);
        await _availability.ValidateDeliveryDateAsync(draft.RequestedDeliveryDate, minimum, ct);
        var now = _clock.UtcNow;
        var abutments = string.Join(",", draft.TeethRange.DefaultAbutments(draft.ConstructionType));
        string code = "";
        for (var i = 0; i < 20; i++)
        {
            code = _codeGenerator.Generate();
            if (!await _repository.OrderCodeExistsAsync(code, ct)) break;
            code = "";
        }
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Could not allocate a unique order code.");
        var order = new OrderRecord(
            0,
            code,
            actor.ClinicCode,
            actor.ClinicDisplayName,
            actor.CredentialId,
            actor.CredentialLabel,
            actor.CredentialPinHashFingerprint,
            draft.CaseName.Trim(),
            draft.ImpressionDate,
            draft.ProductCategory,
            draft.WorkType,
            draft.Material,
            draft.ConstructionType,
            draft.TeethRange.Start,
            draft.TeethRange.End,
            abutments,
            draft.RequestedDeliveryDate,
            OrderStatus.Created,
            string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
            now,
            now,
            ip,
            userAgent);
        return await _repository.CreateOrderAsync(order, ct);
    }

    public Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default) => _repository.GetOrderByCodeAsync(orderCode, ct);
    public Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default) => _repository.ListOrdersAsync(limit, ct);
}
