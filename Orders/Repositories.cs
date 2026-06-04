namespace Orders;

public interface IAuthSessionRepository
{
    Task AddSessionAsync(AuthSession session, CancellationToken ct = default);
    Task<AuthSession?> FindSessionByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task RefreshSessionAsync(string sessionId, DateTimeOffset lastSeenAt, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task RevokeSessionAsync(string sessionId, DateTimeOffset revokedAt, CancellationToken ct = default);
    Task RevokeClinicSessionsAsync(string clinicCode, DateTimeOffset revokedAt, CancellationToken ct = default);
    Task RevokeCredentialSessionsAsync(string clinicCode, string credentialId, DateTimeOffset revokedAt, CancellationToken ct = default);
}

public interface IOrderRepository
{
    Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default);
    Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default);
    Task<OrderRecord> UpdateOrderAsync(OrderRecord order, CancellationToken ct = default);
    Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default);
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
