using System;
using System.IO;
using System.Threading.Tasks;
using Accounting;
using Configuration;
using Invoices;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Database.Tests;

[TestFixture]
[TestOf(typeof(SqliteClientRepo))]
public class SqliteClientRepoTest : Accounting.Tests.ClientRepoContractTest
{
    private string _dbPath = null!;

    protected override async Task<FixtureBase> SetUpAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteClientRepoTest", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        var options = new DbContextOptionsBuilder<Database.AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        await using (var ctx = new Database.AppDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        var contextFactory = () => new Database.AppDbContext(options);
        var clientRepo = new SqliteClientRepo(contextFactory);
        var config = new Config { App = new AppConfig { StartInvoiceNumber = 1 } };
        var invoiceRepo = new SqliteInvoiceRepo(contextFactory, config);

        return new SqliteFixture(clientRepo, invoiceRepo);
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

    private sealed class SqliteFixture : FixtureBase
    {
        private readonly SqliteClientRepo _repo;
        private readonly SqliteInvoiceRepo _invoiceRepo;

        public SqliteFixture(SqliteClientRepo repo, SqliteInvoiceRepo invoiceRepo)
        {
            _repo = repo;
            _invoiceRepo = invoiceRepo;
        }

        public override IClientRepo Repo => _repo;

        public override Task SetUpClientAsync(Client client) =>
            _repo.AddAsync(client);

        public override Task<Client> GetClientAsync(string nickname) =>
            _repo.GetAsync(nickname);

        public override Task<Invoice> SetUpInvoiceAsync(Invoice.InvoiceContent content) =>
            _invoiceRepo.CreateAsync(content);
    }
}
