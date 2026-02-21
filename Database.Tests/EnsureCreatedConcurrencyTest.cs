using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Database.Tests;

/// <summary>
/// Regression test for agent-qa/failures/20260221-0130.resolved.md.
/// Ensures MigrateAsync (replacing EnsureCreated) handles concurrent startup safely.
/// </summary>
[TestFixture]
public class EnsureCreatedConcurrencyTest
{
    [Test]
    [Repeat(10)]
    public async Task MigrateAsync_WhenCalledConcurrently_DoesNotThrow()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "EnsureCreatedConcurrencyTest",
            $"{Guid.NewGuid():N}.db");
        var dbDir = Path.GetDirectoryName(dbPath)!;
        Directory.CreateDirectory(dbDir);

        try
        {
            const int concurrency = 8;
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            var barrier = new Barrier(concurrency);

            // Simulate multiple CLI processes starting at once - each calls MigrateAsync.
            // Barrier ensures all tasks hit MigrateAsync simultaneously.
            var tasks = new Task[concurrency];
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            for (var i = 0; i < concurrency; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        await using var ctx = new AppDbContext(options);
                        barrier.SignalAndWait();
                        await ctx.Database.MigrateAsync();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                        throw;
                    }
                });
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // At least one task failed - collect and report
            }

            foreach (var ex in exceptions)
            {
                Assert.Fail($"MigrateAsync threw when called concurrently: {ex.GetType().Name}: {ex.Message}");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
