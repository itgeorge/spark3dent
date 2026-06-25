using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

namespace Database.Tests;

[TestFixture]
public class SqliteSchedulingWriteTransactionTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteSchedulingWriteTransactionTest", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        await using (var ctx = new AppDbContext(options))
            await ctx.Database.MigrateAsync();
        _contextFactory = () => new AppDbContext(options);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Test]
    public async Task ExecuteAsync_SerializesConcurrentOperations()
    {
        var runner = new SqliteSchedulingWriteTransaction(_contextFactory);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(() => runner.ExecuteAsync(async _ =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
            return 1;
        }));

        await firstEntered.Task;

        var second = Task.Run(() => runner.ExecuteAsync(async _ =>
        {
            secondEntered.SetResult();
            return 2;
        }));

        await Task.Delay(200);
        Assert.That(secondEntered.Task.IsCompleted, Is.False);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.That(secondEntered.Task.IsCompleted, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_TransactionScopedRepositorySeesWritesConsistently()
    {
        var runner = new SqliteSchedulingWriteTransaction(_contextFactory);
        var outsideRepo = new SqliteOrderRepo(_contextFactory);

        var result = await runner.ExecuteAsync(async repo =>
        {
            var before = (await repo.ListActiveOrdersByDeadlineRangeAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10))).Count;
            await repo.CreateOrderAsync(BuildOrder("TX-001", new DateOnly(2026, 6, 10)));
            var after = (await repo.ListActiveOrdersByDeadlineRangeAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10))).Count;
            return (Before: before, After: after);
        });

        Assert.That(result.Before, Is.EqualTo(0));
        Assert.That(result.After, Is.EqualTo(1));
        Assert.That((await outsideRepo.ListActiveOrdersByDeadlineRangeAsync(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10))).Single().OrderCode, Is.EqualTo("TX-001"));
    }

    [Test]
    public async Task ExecuteAsync_WhenOperationThrows_RollsBackWrites()
    {
        var runner = new SqliteSchedulingWriteTransaction(_contextFactory);
        var outsideRepo = new SqliteOrderRepo(_contextFactory);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runner.ExecuteAsync<object?>(async repo =>
            {
                await repo.CreateOrderAsync(BuildOrder("RB-001", new DateOnly(2026, 6, 10)));
                throw new InvalidOperationException("boom");
            }));

        Assert.That(await outsideRepo.GetOrderByCodeAsync("RB-001"), Is.Null);
    }

    private static OrderRecord BuildOrder(string code, DateOnly requestedDeliveryDate) =>
        new(
            0,
            code,
            "DEMO",
            "Demo Clinic",
            "seed",
            "Seed",
            "fingerprint",
            code,
            new DateOnly(2026, 6, 8),
            ProductCategory.Temporary,
            Material.Pmma,
            [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
            requestedDeliveryDate,
            OrderStatus.Created,
            Shade.Unspecified,
            null,
            DateTimeOffset.Parse("2026-06-08T07:30:00Z"),
            DateTimeOffset.Parse("2026-06-08T07:30:00Z"),
            "127.0.0.1",
            "test",
            null,
            1.0m);
}
