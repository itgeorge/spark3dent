using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

namespace Database.Tests;

[TestFixture]
public class SqliteReservationRepoTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteReservationRepoTest", $"{Guid.NewGuid():N}.db");
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
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch { }
    }

    [Test]
    public async Task Migration_CreatesSchedulingReservationsTable()
    {
        await using var ctx = _contextFactory();
        var columns = await ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('SchedulingReservations')").ToListAsync();

        Assert.That(columns, Does.Contain("Id"));
        Assert.That(columns, Does.Contain("ImpressionDate"));
        Assert.That(columns, Does.Contain("RequestedDeliveryDate"));
        Assert.That(columns, Does.Not.Contain("OrderCode"));
    }

    [Test]
    public async Task ListActiveReservationsForActorAsync_DoesNotHideValidRowsBehindExpiredRows()
    {
        var repo = new SqliteReservationRepo(_contextFactory);
        var now = ToUtc(new DateTime(2026, 6, 26, 0, 0, 0, DateTimeKind.Unspecified));
        for (var i = 0; i < 5; i++)
        {
            await repo.CreateReservationAsync(BuildReservation($"expired-{i}", new DateOnly(2026, 6, 24), new DateOnly(2026, 7, 20).AddDays(i)));
        }
        var active = await repo.CreateReservationAsync(BuildReservation("active", new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 27)));

        var listed = await repo.ListActiveReservationsForActorAsync("DEMO", 1, now);

        Assert.That(listed.Select(r => r.Id), Is.EqualTo(new[] { active.Id }));
    }

    [Test]
    public async Task CreateUpdateCancelReservation_RoundTrips()
    {
        var repo = new SqliteReservationRepo(_contextFactory);
        var created = await repo.CreateReservationAsync(BuildReservation("case", new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 29)));

        var updated = await repo.UpdateReservationAsync(created with
        {
            CaseName = "updated",
            Status = ReservationStatus.Cancelled,
            CalculatedCapacityUnits = 2m
        });
        var loaded = await repo.GetReservationByIdAsync(created.Id);

        Assert.That(updated.CaseName, Is.EqualTo("updated"));
        Assert.That(loaded!.Status, Is.EqualTo(ReservationStatus.Cancelled));
        Assert.That(loaded.CalculatedCapacityUnits, Is.EqualTo(2m));
        Assert.That(loaded.WorkItems, Has.Count.EqualTo(1));
    }

    private static DateTimeOffset ToUtc(DateTime labLocal)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(labLocal, LabTimeZone.BulgariaSofia);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static ReservationRecord BuildReservation(string caseName, DateOnly impressionDate, DateOnly deliveryDate, string clinicCode = "DEMO") => new(
        0,
        clinicCode,
        clinicCode == "DEMO" ? "Demo Clinic" : "Other Clinic",
        "member",
        "Member",
        "fingerprint",
        caseName,
        impressionDate,
        ProductCategory.Permanent,
        Material.Pmma,
        [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
        deliveryDate,
        ReservationStatus.Active,
        Shade.Unspecified,
        "notes",
        DateTimeOffset.Parse("2026-06-20T08:00:00Z"),
        DateTimeOffset.Parse("2026-06-20T08:00:00Z"),
        "127.0.0.1",
        "test",
        "color",
        1m);
}
