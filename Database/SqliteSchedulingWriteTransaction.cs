using Orders;

namespace Database;

public sealed class SqliteSchedulingWriteTransaction : ISchedulingWriteTransaction
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteSchedulingWriteTransaction(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<T> ExecuteAsync<T>(Func<IOrderRepository, Task<T>> operation, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        return await SqliteImmediateTransaction.ExecuteAsync(ctx, async sharedContext =>
        {
            var repository = new SqliteOrderRepo(sharedContext);
            return await operation(repository);
        }, ct);
    }
}
