namespace Orders;

public interface ISchedulingWriteTransaction
{
    Task<T> ExecuteAsync<T>(Func<IOrderRepository, Task<T>> operation, CancellationToken ct = default);

    Task<T> ExecuteAsync<T>(Func<IOrderRepository, IReservationRepository, Task<T>> operation, CancellationToken ct = default) =>
        ExecuteAsync(async orders => await operation(orders, NullReservationRepository.Instance), ct);
}

internal sealed class NullReservationRepository : IReservationRepository
{
    public static readonly NullReservationRepository Instance = new();

    private NullReservationRepository() { }

    public Task<ReservationRecord> CreateReservationAsync(ReservationRecord reservation, CancellationToken ct = default) =>
        throw new InvalidOperationException("Reservation repository is not configured.");

    public Task<ReservationRecord?> GetReservationByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<ReservationRecord?>(null);

    public Task<ReservationRecord> UpdateReservationAsync(ReservationRecord reservation, CancellationToken ct = default) =>
        throw new InvalidOperationException("Reservation repository is not configured.");

    public Task<IReadOnlyList<ReservationRecord>> ListActiveReservationsForActorAsync(string? clinicCode, int limit, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReservationRecord>>([]);

    public Task<IReadOnlyList<ReservationRecord>> ListActiveReservationsForCalendarAsync(string? clinicCode, DateOnly start, DateOnly end, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReservationRecord>>([]);

    public Task<IReadOnlyList<ReservationRecord>> ListActiveReservationsByDeadlineRangeAsync(DateOnly start, DateOnly end, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReservationRecord>>([]);
}
