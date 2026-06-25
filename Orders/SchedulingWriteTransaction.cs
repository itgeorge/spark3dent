namespace Orders;

public interface ISchedulingWriteTransaction
{
    Task<T> ExecuteAsync<T>(Func<IOrderRepository, Task<T>> operation, CancellationToken ct = default);
}
