using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

namespace Database.Tests;

[TestFixture]
public class SqliteMaterialSchedulingConfigProviderTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteMaterialSchedulingConfigProviderTest", $"{Guid.NewGuid():N}.db");
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
    public async Task Migration_SeedsInitialSchedulingMaterialConfigs()
    {
        await using var ctx = _contextFactory();

        var rows = await ctx.SchedulingMaterialConfigs.AsNoTracking().OrderBy(c => c.Material).ToListAsync();

        Assert.That(rows.Select(r => r.Material), Is.EqualTo(new[]
        {
            Material.FullContourZirconia,
            Material.GlassCeramics,
            Material.Pfm,
            Material.PfzLayeredZrCrown,
            Material.Pmma,
            Material.PmmaTelio
        }));
        var pmma = rows.Single(r => r.Material == Material.Pmma);
        Assert.Multiple(() =>
        {
            Assert.That(pmma.FixedLeadTimeBusinessDays, Is.EqualTo(2));
            Assert.That(pmma.CapacityUnitsPerTooth, Is.EqualTo(1.0m));
            Assert.That(pmma.TeethPerExtraLeadDay, Is.Null);
            Assert.That(pmma.ActiveFromDate, Is.EqualTo(new DateOnly(2026, 1, 1)));
        });
        var pfm = rows.Single(r => r.Material == Material.Pfm);
        Assert.Multiple(() =>
        {
            Assert.That(pfm.FixedLeadTimeBusinessDays, Is.EqualTo(4));
            Assert.That(pfm.TeethPerExtraLeadDay, Is.EqualTo(10));
            Assert.That(pfm.CapacityUnitsPerTooth, Is.EqualTo(1.0m));
        });
    }

    [Test]
    public async Task CreateAsync_CreatesFirstRowForMissingMaterial()
    {
        await using (var ctx = _contextFactory())
        {
            var rows = await ctx.SchedulingMaterialConfigs.Where(c => c.Material == Material.PmmaTelio).ToListAsync();
            ctx.SchedulingMaterialConfigs.RemoveRange(rows);
            await ctx.SaveChangesAsync();
        }
        var provider = new SqliteMaterialSchedulingConfigProvider(_contextFactory);

        var created = await provider.CreateAsync(Material.PmmaTelio, new MaterialSchedulingConfigCreate(2, 1.1m, null), DateTimeOffset.Parse("2026-06-22T00:00:00Z"));

        Assert.Multiple(() =>
        {
            Assert.That(created.Material, Is.EqualTo(Material.PmmaTelio));
            Assert.That(created.FixedLeadTimeBusinessDays, Is.EqualTo(2));
            Assert.That(created.CapacityUnitsPerTooth, Is.EqualTo(1.1m));
            Assert.That(created.ActiveFromDate, Is.EqualTo(new DateOnly(2026, 6, 22)));
        });
    }

    [Test]
    public async Task CreateAsync_UsesLabLocalDateForActiveFromDate()
    {
        await using (var ctx = _contextFactory())
        {
            var rows = await ctx.SchedulingMaterialConfigs.Where(c => c.Material == Material.PmmaTelio).ToListAsync();
            ctx.SchedulingMaterialConfigs.RemoveRange(rows);
            await ctx.SaveChangesAsync();
        }
        var provider = new SqliteMaterialSchedulingConfigProvider(_contextFactory);

        var created = await provider.CreateAsync(Material.PmmaTelio, new MaterialSchedulingConfigCreate(2, 1.1m, null), DateTimeOffset.Parse("2026-06-21T21:30:00Z"));

        Assert.That(created.ActiveFromDate, Is.EqualTo(new DateOnly(2026, 6, 22)));
    }

    [Test]
    public async Task GetForDateAsync_UsesLatestMaterialRowOnOrBeforeDeadlineDate()
    {
        await using (var ctx = _contextFactory())
        {
            ctx.SchedulingMaterialConfigs.Add(new SchedulingMaterialConfigEntity
            {
                Material = Material.Pmma,
                ActiveFromDate = new DateOnly(2026, 7, 1),
                FixedLeadTimeBusinessDays = 4,
                CapacityUnitsPerTooth = 2.5m,
                CreatedAt = DateTimeOffset.Parse("2026-06-22T00:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-06-22T00:00:00Z")
            });
            await ctx.SaveChangesAsync();
        }
        var provider = new SqliteMaterialSchedulingConfigProvider(_contextFactory);

        var before = await provider.GetForDateAsync(Material.Pmma, new DateOnly(2026, 6, 30));
        var onOrAfter = await provider.GetForDateAsync(Material.Pmma, new DateOnly(2026, 7, 1));

        Assert.Multiple(() =>
        {
            Assert.That(before.FixedLeadTimeBusinessDays, Is.EqualTo(2));
            Assert.That(before.CapacityUnitsPerTooth, Is.EqualTo(1.0m));
            Assert.That(onOrAfter.FixedLeadTimeBusinessDays, Is.EqualTo(4));
            Assert.That(onOrAfter.CapacityUnitsPerTooth, Is.EqualTo(2.5m));
        });
    }

    [Test]
    public async Task UpdateAsync_UsesLabLocalDateForNewEffectiveRow()
    {
        var provider = new SqliteMaterialSchedulingConfigProvider(_contextFactory);

        var updated = await provider.UpdateAsync(Material.Pmma, new MaterialSchedulingConfigUpdate(5, 1.25m, null), DateTimeOffset.Parse("2026-06-21T21:30:00Z"));

        Assert.That(updated.ActiveFromDate, Is.EqualTo(new DateOnly(2026, 6, 22)));
    }

    [Test]
    public async Task ListAsync_ReturnsOnlyLatestMaterialRows()
    {
        await using (var ctx = _contextFactory())
        {
            ctx.SchedulingMaterialConfigs.Add(new SchedulingMaterialConfigEntity
            {
                Material = Material.Pmma,
                ActiveFromDate = new DateOnly(2026, 7, 1),
                FixedLeadTimeBusinessDays = 4,
                CapacityUnitsPerTooth = 2.5m,
                CreatedAt = DateTimeOffset.Parse("2026-06-22T00:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-06-22T00:00:00Z")
            });
            await ctx.SaveChangesAsync();
        }
        var provider = new SqliteMaterialSchedulingConfigProvider(_contextFactory);

        var rows = await provider.ListAsync();

        Assert.That(rows.Count(r => r.Material == Material.Pmma), Is.EqualTo(1));
        Assert.That(rows.Single(r => r.Material == Material.Pmma).FixedLeadTimeBusinessDays, Is.EqualTo(4));
    }

    [Test]
    public async Task GetLatestAsync_ReturnsSeededPfmConfig()
    {
        var provider = new SqliteMaterialSchedulingConfigProvider(_contextFactory);

        var config = await provider.GetLatestAsync(Material.Pfm);

        Assert.Multiple(() =>
        {
            Assert.That(config.Material, Is.EqualTo(Material.Pfm));
            Assert.That(config.FixedLeadTimeBusinessDays, Is.EqualTo(4));
            Assert.That(config.TeethPerExtraLeadDay, Is.EqualTo(10));
            Assert.That(config.CapacityUnitsPerTooth, Is.EqualTo(1.0m));
        });
    }

    [Test]
    public async Task GetLatestAsync_ReflectsDatabaseEditsWithoutHardcodedFallback()
    {
        await using (var ctx = _contextFactory())
        {
            var pmma = await ctx.SchedulingMaterialConfigs.SingleAsync(c => c.Material == Material.Pmma);
            pmma.FixedLeadTimeBusinessDays = 5;
            pmma.CapacityUnitsPerTooth = 1.25m;
            await ctx.SaveChangesAsync();
        }
        var provider = new SqliteMaterialSchedulingConfigProvider(_contextFactory);

        var config = await provider.GetLatestAsync(Material.Pmma);

        Assert.That(config.FixedLeadTimeBusinessDays, Is.EqualTo(5));
        Assert.That(config.CapacityUnitsPerTooth, Is.EqualTo(1.25m));
    }

    [Test]
    public async Task GetLatestAsync_GivenMissingRow_FailsClearly()
    {
        await using (var ctx = _contextFactory())
        {
            var row = await ctx.SchedulingMaterialConfigs.SingleAsync(c => c.Material == Material.Pmma);
            ctx.SchedulingMaterialConfigs.Remove(row);
            await ctx.SaveChangesAsync();
        }
        var provider = new SqliteMaterialSchedulingConfigProvider(_contextFactory);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetLatestAsync(Material.Pmma));

        Assert.That(ex!.Message, Does.Contain("missing").IgnoreCase);
        Assert.That(ex.Message, Does.Contain(nameof(Material.Pmma)));
    }
}
