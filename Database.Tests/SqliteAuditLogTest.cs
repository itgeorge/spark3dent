using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Utilities;

namespace Database.Tests;

[TestFixture]
public class SqliteAuditLogTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "AuditTests", Guid.NewGuid() + ".db");
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
        try { File.Delete(_dbPath); } catch { /* ignore */ }
    }

    [Test]
    public async Task AppendAsync_PersistsAuditEventWithIndexesQueryableByEntity()
    {
        var repo = new SqliteAuditLog(_contextFactory);
        var occurredAt = new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero);

        await repo.AppendAsync(new AuditEvent(
            0,
            "Scheduling",
            "OrderCreated",
            "SchedulingOrder",
            "ABC-123",
            "Case A",
            "Technician",
            "DEMO",
            "tech-1",
            "Technician",
            "session-1",
            occurredAt,
            "127.0.0.1",
            "test-agent",
            "{\"targetClinicCode\":\"OTHER\"}"));

        await using var ctx = _contextFactory();
        var saved = await ctx.AuditEvents.SingleAsync(e => e.EntityType == "SchedulingOrder" && e.EntityId == "ABC-123");
        Assert.That(saved.Id, Is.GreaterThan(0));
        Assert.That(saved.Operation, Is.EqualTo("OrderCreated"));
        Assert.That(saved.ActorCredentialId, Is.EqualTo("tech-1"));
        Assert.That(saved.OccurredAtUnixTimeMilliseconds, Is.EqualTo(occurredAt.ToUnixTimeMilliseconds()));
        Assert.That(saved.MetadataJson, Does.Contain("OTHER"));
    }

    [Test]
    public async Task AppendAsync_AppendsRowsWithoutMutatingExistingRows()
    {
        var repo = new SqliteAuditLog(_contextFactory);

        await repo.AppendAsync(CreateEvent("OrderCreated"));
        await repo.AppendAsync(CreateEvent("OrderUpdated"));

        await using var ctx = _contextFactory();
        var rows = await ctx.AuditEvents.OrderBy(e => e.Id).ToListAsync();
        Assert.That(rows.Select(r => r.Operation), Is.EqualTo(new[] { "OrderCreated", "OrderUpdated" }));
        Assert.That(rows.Select(r => r.Id), Is.Unique);
    }

    private static AuditEvent CreateEvent(string operation) => new(
        0,
        "Scheduling",
        operation,
        "SchedulingOrder",
        "ABC-123",
        "Case A",
        "Clinic",
        "DEMO",
        "cred-1",
        "Cred 1",
        "session-1",
        DateTimeOffset.UtcNow,
        null,
        null,
        null);
}
