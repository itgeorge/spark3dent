using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration;
using Invoices;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Database.Tests;

[TestFixture]
[TestOf(typeof(SqliteInvoiceRepo))]
public class SqliteInvoiceRepoTest : Invoices.Tests.InvoiceRepoContractTest
{
    private string _dbPath = null!;
    private Func<Database.AppDbContext> _contextFactory = null!;

    protected override async Task<FixtureBase> SetUpAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteInvoiceRepoTest", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        var options = new DbContextOptionsBuilder<Database.AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        await using (var ctx = new Database.AppDbContext(options))
        {
            await ctx.Database.EnsureCreatedAsync();
        }

        _contextFactory = () => new Database.AppDbContext(options);

        var config = new Config { App = new AppConfig { StartInvoiceNumber = 1 } };
        var repo = new SqliteInvoiceRepo(_contextFactory, config);

        return new SqliteFixture(repo);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Test]
    public async Task Create_GivenFreshDbAndStartInvoiceNumber1_WhenCreatingFirstInvoice_ThenNumberIs1()
    {
        var (repo, _) = await CreateFreshRepoAsync(startInvoiceNumber: 1);
        var content = BuildValidInvoiceContent();

        var created = await repo.CreateAsync(content);

        Assert.That(created.Number, Is.EqualTo("1"));
    }

    [Test]
    public async Task Create_GivenFreshDbAndStartInvoiceNumber1000_WhenCreatingFirstInvoice_ThenNumberIs1000()
    {
        var (repo, _) = await CreateFreshRepoAsync(startInvoiceNumber: 1000);
        var content = BuildValidInvoiceContent();

        var created = await repo.CreateAsync(content);

        Assert.That(created.Number, Is.EqualTo("1000"));
    }

    [Test]
    public async Task Create_GivenExistingInvoice_WhenCreatingWithChangedConfigStartInvoiceNumber_ThenNumberFollowsSequenceNotConfig()
    {
        var (repo1, dbPath) = await CreateFreshRepoAsync(startInvoiceNumber: 1);
        await repo1.CreateAsync(BuildValidInvoiceContent());
        var options = new DbContextOptionsBuilder<Database.AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var config1000 = new Config { App = new AppConfig { StartInvoiceNumber = 1000 } };
        var repo2 = new SqliteInvoiceRepo(() => new Database.AppDbContext(options), config1000);

        var created = await repo2.CreateAsync(BuildValidInvoiceContent(date: DateTime.UtcNow.AddDays(1)));

        Assert.That(created.Number, Is.EqualTo("2"), "Sequence table is source of truth, not config");
    }

    [Test]
    public async Task Create_GivenConcurrentCreateCalls_WhenRacing_ThenAllNumbersUniqueAndContiguous()
    {
        var (repo, _) = await CreateFreshRepoAsync(startInvoiceNumber: 1);
        var content = BuildValidInvoiceContent();
        var racers = Math.Min(8, 2 * Environment.ProcessorCount);
        var barrier = new Barrier(racers);
        var created = new System.Collections.Concurrent.ConcurrentBag<Invoice>();

        var tasks = Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            created.Add(await repo.CreateAsync(content));
        })).ToList();
        await Task.WhenAll(tasks);

        var numbers = created.Select(c => long.Parse(c.Number)).OrderBy(n => n).ToList();
        Assert.That(numbers, Has.Count.EqualTo(racers));
        for (var i = 0; i < racers; i++)
            Assert.That(numbers[i], Is.EqualTo(i + 1));
    }

    private async Task<(SqliteInvoiceRepo Repo, string DbPath)> CreateFreshRepoAsync(int startInvoiceNumber)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteInvoiceRepoTest", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var options = new DbContextOptionsBuilder<Database.AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using (var ctx = new Database.AppDbContext(options))
            await ctx.Database.EnsureCreatedAsync();
        var config = new Config { App = new AppConfig { StartInvoiceNumber = startInvoiceNumber } };
        var repo = new SqliteInvoiceRepo(() => new Database.AppDbContext(options), config);
        return (repo, dbPath);
    }

    private sealed class SqliteFixture : FixtureBase
    {
        private readonly SqliteInvoiceRepo _repo;

        public SqliteFixture(SqliteInvoiceRepo repo) => _repo = repo;

        public override IInvoiceRepo Repo => _repo;

        public override Task<Invoice> SetUpInvoiceAsync(Invoice.InvoiceContent content) =>
            _repo.CreateAsync(content);

        public override Task<Invoice> GetInvoiceAsync(string number) =>
            _repo.GetAsync(number);
    }
}
