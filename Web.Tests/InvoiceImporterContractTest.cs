using Accounting;
using Invoices;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Storage;
using Utilities;
using Web;

namespace Web.Tests;

[TestFixture]
[TestOf(typeof(IInvoiceImporter))]
public abstract class InvoiceImporterContractTest
{
    protected abstract Task<FixtureBase> SetUpAsync();

    protected abstract class FixtureBase
    {
        public abstract IInvoiceImporter Importer { get; }
        public abstract void SetParserResults(params LegacyInvoiceData?[] results);
        public abstract Task SeedClientAsync(Client client);
        public abstract void SetDuplicateInvoices(params string[] numbers);
        public abstract IReadOnlyCollection<string> AddedClientNicknames { get; }
        public abstract IReadOnlyCollection<string> ImportedInvoiceNumbers { get; }
        public abstract IReadOnlyCollection<Currency> ImportedCurrencies { get; }
    }

    [Test]
    public async Task Analyze_WhenParseFails_ReturnsFileError()
    {
        var fixture = await SetUpAsync();
        fixture.SetParserResults((LegacyInvoiceData?)null);

        var req = new ImportAnalyzeRequest([CreatePdfFile("a.pdf")], new ImportAnalyzeOptions());
        var res = await fixture.Importer.AnalyzeAsync(req);

        Assert.That(res.Files, Has.Length.EqualTo(1));
        Assert.That(res.Files[0].Error, Is.Not.Null.And.Not.Empty);
        Assert.That(res.UnresolvedCompanies, Is.Empty);
    }

    [Test]
    public async Task Analyze_WhenClientMissing_ReturnsUnresolvedCompany()
    {
        var fixture = await SetUpAsync();
        fixture.SetParserResults(TestData(number: "100", eik: "BG123"));

        var req = new ImportAnalyzeRequest([CreatePdfFile("a.pdf")], new ImportAnalyzeOptions());
        var res = await fixture.Importer.AnalyzeAsync(req);

        Assert.That(res.UnresolvedCompanies, Is.EquivalentTo(new[] { "BG123" }));
        Assert.That(res.Files[0].InvoiceNumber, Is.EqualTo("100"));
    }

    [Test]
    public async Task Analyze_WhenParserReturnsBgn_ReturnsCurrencyInFileResult()
    {
        var fixture = await SetUpAsync();
        fixture.SetParserResults(TestDataWithCurrency(number: "101", eik: "BG101", currency: Currency.Bgn));

        var req = new ImportAnalyzeRequest([CreatePdfFile("bgn.pdf")], new ImportAnalyzeOptions());
        var res = await fixture.Importer.AnalyzeAsync(req);

        Assert.That(res.Files, Has.Length.EqualTo(1));
        Assert.That(res.Files[0].Error, Is.Null);
        Assert.That(res.Files[0].Currency, Is.EqualTo("Bgn"));
    }

    [Test]
    public async Task Analyze_WhenParserReturnsEur_ReturnsCurrencyInFileResult()
    {
        var fixture = await SetUpAsync();
        fixture.SetParserResults(TestDataWithCurrency(number: "102", eik: "BG102", currency: Currency.Eur));

        var req = new ImportAnalyzeRequest([CreatePdfFile("eur.pdf")], new ImportAnalyzeOptions());
        var res = await fixture.Importer.AnalyzeAsync(req);

        Assert.That(res.Files, Has.Length.EqualTo(1));
        Assert.That(res.Files[0].Error, Is.Null);
        Assert.That(res.Files[0].Currency, Is.EqualTo("Eur"));
    }

    [Test]
    public async Task Commit_WhenItemHasBgnCurrency_ImportsWithBgnCurrency()
    {
        var fixture = await SetUpAsync();
        var req = new ImportCommitRequest(
            [new ImportCommitItem("bgn.pdf", null, "200", "2026-01-01", 50000, "Bgn", "BG200", "Acme Ltd", "Ivan Petrov", "addr", "Sofia", "1000", "Bulgaria")],
            new Dictionary<string, string>(),
            false);

        var res = await fixture.Importer.CommitAsync(req);

        Assert.That(res.Imported, Is.EqualTo(1));
        Assert.That(fixture.ImportedInvoiceNumbers, Does.Contain("200"));
        Assert.That(fixture.ImportedCurrencies, Does.Contain(Currency.Bgn));
    }

    [Test]
    public async Task Commit_WhenMissingClient_CreatesClientAndImports()
    {
        var fixture = await SetUpAsync();
        var req = new ImportCommitRequest(
            [new ImportCommitItem("a.pdf", null, "200", "2026-01-01", 12345, "Eur", "BG200", "Acme Ltd", "Ivan Petrov", "addr", "Sofia", "1000", "Bulgaria")],
            new Dictionary<string, string>(),
            false);

        var res = await fixture.Importer.CommitAsync(req);

        Assert.That(res.Imported, Is.EqualTo(1));
        Assert.That(res.Skipped, Is.EqualTo(0));
        Assert.That(res.Failed, Is.EqualTo(0));
        Assert.That(fixture.ImportedInvoiceNumbers, Does.Contain("200"));
        Assert.That(fixture.AddedClientNicknames, Does.Contain("ivan-petrov"));
    }

    [Test]
    public async Task Commit_WhenItemHasNullRepresentativeNameAndPostalCode_ImportsWithEmptyStrings()
    {
        var fixture = await SetUpAsync();
        var req = new ImportCommitRequest(
            [new ImportCommitItem("a.pdf", null, "201", "2026-01-01", 12345, "Eur", "BG201", "Acme Ltd", null, "addr", "Sofia", null, "Bulgaria")],
            new Dictionary<string, string>(),
            false);

        var res = await fixture.Importer.CommitAsync(req);

        Assert.That(res.Imported, Is.EqualTo(1));
        Assert.That(res.Failed, Is.EqualTo(0));
        Assert.That(fixture.ImportedInvoiceNumbers, Does.Contain("201"));
        Assert.That(fixture.AddedClientNicknames, Does.Contain("acme-ltd"));
    }

    [Test]
    public async Task Commit_WhenInvoiceAlreadyExists_Skips()
    {
        var fixture = await SetUpAsync();
        fixture.SetDuplicateInvoices("300");
        var req = new ImportCommitRequest(
            [new ImportCommitItem("a.pdf", null, "300", "2026-01-01", 12345, "Eur", "BG300", "Acme Ltd", "Ivan", "addr", "Sofia", "1000", "Bulgaria")],
            new Dictionary<string, string>(),
            false);

        var res = await fixture.Importer.CommitAsync(req);

        Assert.That(res.Imported, Is.EqualTo(0));
        Assert.That(res.Skipped, Is.EqualTo(1));
        Assert.That(res.Failed, Is.EqualTo(0));
    }

    [Test]
    public async Task Commit_MixedOutcomes_ReturnsMixedSummary()
    {
        var fixture = await SetUpAsync();
        fixture.SetDuplicateInvoices("401");
        var req = new ImportCommitRequest(
        [
            new ImportCommitItem("ok.pdf", null, "400", "2026-01-01", 100, "Eur", "BG400", "A", "Rep A", "addr", "Sofia", "1000", "Bulgaria"),
            new ImportCommitItem("dup.pdf", null, "401", "2026-01-01", 100, "Eur", "BG401", "B", "Rep B", "addr", "Sofia", "1000", "Bulgaria"),
            new ImportCommitItem("bad.pdf", null, null, "2026-01-01", 100, "Eur", "BG402", "C", "Rep C", "addr", "Sofia", "1000", "Bulgaria")
        ],
            new Dictionary<string, string>(),
            false);

        var res = await fixture.Importer.CommitAsync(req);

        Assert.That(res.Imported, Is.EqualTo(1));
        Assert.That(res.Skipped, Is.EqualTo(1));
        Assert.That(res.Failed, Is.EqualTo(1));
        Assert.That(res.ItemStatuses, Has.Length.EqualTo(3));
    }

    protected static IFormFile CreatePdfFile(string fileName)
    {
        var bytes = new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'4' };
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "files", fileName);
    }

    protected static LegacyInvoiceData TestData(string number, string eik) =>
        new(number, new DateTime(2026, 1, 1), 12345, Currency.Eur, new BillingAddress("Acme", "Ivan Petrov", eik, null, "addr", "Sofia", "1000", "Bulgaria"));

    protected static LegacyInvoiceData TestDataWithCurrency(string number, string eik, Currency currency) =>
        new(number, new DateTime(2026, 1, 1), 12345, currency, new BillingAddress("Acme", "Ivan Petrov", eik, null, "addr", "Sofia", "1000", "Bulgaria"));

    protected sealed class SequentialParser : ILegacyInvoiceParser
    {
        private readonly Queue<LegacyInvoiceData?> _results = new();
        public void SetResults(params LegacyInvoiceData?[] results)
        {
            _results.Clear();
            if (results == null)
                return;
            foreach (var r in results) _results.Enqueue(r);
        }

        public Task<LegacyInvoiceData?> TryParseAsync(byte[] pdfBytes, CancellationToken cancellationToken = default)
            => Task.FromResult(_results.Count == 0 ? null : _results.Dequeue());
    }

    protected sealed class FakeClientRepoImpl : IClientRepo
    {
        private readonly Dictionary<string, Client> _byNickname = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> AddedNicknames = new();

        public Task<Client?> FindByCompanyIdentifierAsync(string companyIdentifier) =>
            Task.FromResult(_byNickname.Values.FirstOrDefault(c => c.Address.CompanyIdentifier.Equals(companyIdentifier, StringComparison.OrdinalIgnoreCase)));

        public Task<Client> GetAsync(string nickname) =>
            _byNickname.TryGetValue(nickname, out var c) ? Task.FromResult(c) : throw new InvalidOperationException("not found");

        public Task<QueryResult<Client>> ListAsync(int limit, string? startAfterCursor = null) =>
            Task.FromResult(new QueryResult<Client>([], null));

        public Task<QueryResult<Client>> LatestAsync(int limit, string? startAfterCursor = null) =>
            Task.FromResult(new QueryResult<Client>([], null));

        public Task AddAsync(Client client)
        {
            if (_byNickname.ContainsKey(client.Nickname))
                throw new InvalidOperationException("already exists");
            _byNickname[client.Nickname] = client;
            AddedNicknames.Add(client.Nickname);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(string nickname, IClientRepo.ClientUpdate update) => throw new NotImplementedException();
        public Task DeleteAsync(string nickname) => throw new NotImplementedException();
    }

    protected sealed class FakeInvoiceOps : IInvoiceOperations
    {
        private readonly HashSet<string> _duplicateNumbers = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> ImportedNumbers = new();
        public void SetDuplicates(params string[] numbers)
        {
            _duplicateNumbers.Clear();
            foreach (var n in numbers) _duplicateNumbers.Add(n);
        }

        public readonly List<Currency> ImportedCurrencies = new();
        public Task<Invoice> ImportLegacyInvoiceAsync(LegacyInvoiceData data, byte[]? sourcePdfBytes = null)
        {
            if (_duplicateNumbers.Contains(data.Number))
                throw new InvalidOperationException("already exists");
            ImportedNumbers.Add(data.Number);
            ImportedCurrencies.Add(data.Currency);
            return Task.FromResult(new Invoice(data.Number, new Invoice.InvoiceContent(
                data.Date,
                data.Recipient,
                data.Recipient,
                [new Invoice.LineItem("x", new Amount(data.TotalCents, data.Currency))],
                new BankTransferInfo("x", "x", "x")), isLegacy: true));
        }

        public Task<QueryResult<Invoice>> ListInvoicesAsync(int limit, string? startAfterCursor) => throw new NotImplementedException();
        public Task<Invoice> GetInvoiceAsync(string number) => throw new NotImplementedException();
        public Task<InvoiceOperationResult> IssueInvoiceAsync(string clientNickname, int amountCents, DateTime? date, IInvoiceExporter exporter) => throw new NotImplementedException();
        public Task<InvoiceOperationResult> CorrectInvoiceAsync(string invoiceNumber, int? amountCents, DateTime? date, IInvoiceExporter exporter) => throw new NotImplementedException();
        public Task<InvoiceOperationResult> ReExportInvoiceAsync(string invoiceNumber, IInvoiceExporter exporter) => throw new NotImplementedException();
        public Task<ExportResult> PreviewInvoiceAsync(string clientNickname, int amountCents, DateTime? date, IInvoiceExporter exporter, string? invoiceNumber = null) => throw new NotImplementedException();
        public Task<(Stream Stream, string DownloadFileName)> GetInvoicePdfStreamAsync(string number) => throw new NotImplementedException();
    }

    protected sealed class FakeBlobStorage : IBlobStorage
    {
        private readonly Dictionary<(string Bucket, string Key), byte[]> _store = new();

        public Task<string> UploadAsync(string bucket, string objectKey, Stream content, string contentType)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            _store[(bucket, objectKey)] = ms.ToArray();
            return Task.FromResult($"{bucket}/{objectKey}");
        }

        public Task<string> CreateUploadUrlAsync(string bucket, string objectKey, TimeSpan ttl, string contentType) =>
            throw new NotImplementedException();

        public Task<Stream> OpenReadAsync(string bucket, string objectKey)
        {
            if (!_store.TryGetValue((bucket, objectKey), out var bytes))
                throw new FileNotFoundException("Object not found", objectKey);
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task<bool> ExistsAsync(string bucket, string objectKey) =>
            Task.FromResult(_store.ContainsKey((bucket, objectKey)));

        public Task DeleteAsync(string bucket, string objectKey)
        {
            _store.Remove((bucket, objectKey));
            return Task.CompletedTask;
        }

        public Task RenameAsync(string bucket, string sourceObjectKey, string destinationObjectKey)
        {
            if (_store.TryGetValue((bucket, sourceObjectKey), out var bytes))
            {
                _store[(bucket, destinationObjectKey)] = bytes;
                _store.Remove((bucket, sourceObjectKey));
            }
            return Task.CompletedTask;
        }

        public string UriFor(string bucket, string objectKey) => $"fake://{bucket}/{objectKey}";

        public Task<IBlobStorage.BlobList> ListAsync(string bucket, string prefix, int limit = 1000, string? cursor = null)
        {
            var keys = _store.Keys
                .Where(x => x.Bucket == bucket && x.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(x => x.Key)
                .OrderBy(x => x, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(new IBlobStorage.BlobList(keys, null));
        }
    }
}
