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
    public static async Task ExecuteAsync(
        AppDbContext ctx,
        Func<AppDbContext, Task> operation)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();

        var sqliteConn = (SqliteConnection)conn;
        await using var tx = sqliteConn.BeginTransaction(IsolationLevel.Serializable, deferred: false);

        await ctx.Database.UseTransactionAsync(tx);

        try
        {
            await operation(ctx);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
