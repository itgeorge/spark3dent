using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Database;

/// <summary>
/// Runs a database operation inside a SQLite IMMEDIATE transaction.
/// Acquires the write lock at transaction start, blocking other writers until commit.
/// Works across multiple app instances sharing the same database file.
/// </summary>
public static class SqliteImmediateTransaction
{
    /// <summary>
    /// Executes <paramref name="operation"/> within a BEGIN IMMEDIATE transaction.
    /// The operation receives the context; it should perform reads/writes and call SaveChangesAsync.
    /// </summary>
    public static Task ExecuteAsync(
        AppDbContext ctx,
        Func<AppDbContext, Task> operation,
        CancellationToken ct = default) =>
        ExecuteAsync<object?>(ctx, async c =>
        {
            await operation(c);
            return null;
        }, ct);

    public static async Task<T> ExecuteAsync<T>(
        AppDbContext ctx,
        Func<AppDbContext, Task<T>> operation,
        CancellationToken ct = default)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        var sqliteConn = (SqliteConnection)conn;
        await using var tx = sqliteConn.BeginTransaction(IsolationLevel.Serializable, deferred: false);

        await ctx.Database.UseTransactionAsync(tx, ct);

        try
        {
            var result = await operation(ctx);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
