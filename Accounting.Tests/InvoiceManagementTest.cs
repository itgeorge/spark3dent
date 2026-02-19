using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Accounting.Tests.Fakes;
using Invoices;
using Invoices.Tests.Fakes;
using NUnit.Framework;
using Storage;
using Utilities.Tests;

namespace Accounting.Tests;

[TestFixture]
[TestOf(typeof(InvoiceManagement))]
public class InvoiceManagementTest
{
    private const string InvoicesBucket = "invoices";
    private static readonly BillingAddress SellerAddress = new(
        Name: "Test Seller EOOD",
        RepresentativeName: "Иван Продавач",
        CompanyIdentifier: "111222333",
        VatIdentifier: null,
        Address: "ул. Продавска 1",
        City: "София",
        PostalCode: "1000",
        Country: "BG");
    private static readonly BankTransferInfo BankInfo = new(
        Iban: "BG00TEST12345678901234",
        BankName: "Test Bank AD",
        Bic: "TESTBGSF");

    private static readonly string LineItemDescription = "Зъботехнически услуги";

    [Test]
    public async Task IssueInvoiceAsync_GivenValidClientAndAmount_WhenIssuing_ThenCreatesInvoiceExportsPdfStoresInBlobStorageAndReturnsPath()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", new BillingAddress(
            Name: "ACME EOOD",
            RepresentativeName: "Мария Купувач",
            CompanyIdentifier: "444555666",
            VatIdentifier: null,
            Address: "ул. Купуванска 42",
            City: "Пловдив",
            PostalCode: "4000",
            Country: "BG"));
        await fixture.ClientRepo.AddAsync(client);

        var result = await fixture.Management.IssueInvoiceAsync("acme", 150_00, null, fixture.Exporter);

        Assert.That(result.Invoice, Is.Not.Null);
        Assert.That(result.Invoice.Number, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(150_00));
        Assert.That(result.Invoice.Content.BuyerAddress.Name, Is.EqualTo("ACME EOOD"));
        Assert.That(result.Invoice.Content.SellerAddress, Is.EqualTo(SellerAddress));
        Assert.That(result.Invoice.Content.LineItems, Has.Length.EqualTo(1));
        Assert.That(result.Invoice.Content.LineItems[0].Description, Is.EqualTo(LineItemDescription));
        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.PdfPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(result.PdfPath), Is.True);
        var stored = await fixture.InvoiceRepo.GetAsync(result.Invoice.Number);
        Assert.That(stored.Number, Is.EqualTo(result.Invoice.Number));
    }

    [Test]
    public void IssueInvoiceAsync_GivenNonExistingClient_WhenIssuing_ThenThrows()
    {
        var fixture = CreateFixture();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Management.IssueInvoiceAsync("nonexistent", 100_00, null, fixture.Exporter));
    }

    [Test]
    public async Task IssueInvoiceAsync_GivenNullDate_WhenIssuing_ThenUsesToday()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);

        var result = await fixture.Management.IssueInvoiceAsync("acme", 100_00, null, fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.Content.Date.Date, Is.EqualTo(DateTime.UtcNow.Date));
    }

    [Test]
    public async Task IssueInvoiceAsync_GivenValidClient_WhenIssuing_ThenBankTransferInfoFromConfig()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);

        var result = await fixture.Management.IssueInvoiceAsync("acme", 100_00, null, fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.Content.BankTransferInfo, Is.EqualTo(BankInfo));
    }

    [Test]
    public async Task IssueInvoiceAsync_GivenMultipleClients_WhenIssuing_ThenEachInvoiceHasCorrectBuyerAddress()
    {
        var fixture = await CreateFixtureAsync();
        var acmeAddress = new BillingAddress(
            Name: "ACME EOOD",
            RepresentativeName: "Alice",
            CompanyIdentifier: "111",
            VatIdentifier: null,
            Address: "Acme St 1",
            City: "Sofia",
            PostalCode: "1000",
            Country: "BG");
        var betaAddress = new BillingAddress(
            Name: "Beta LLC",
            RepresentativeName: "Bob",
            CompanyIdentifier: "222",
            VatIdentifier: null,
            Address: "Beta Ave 2",
            City: "Plovdiv",
            PostalCode: "4000",
            Country: "BG");
        await fixture.ClientRepo.AddAsync(new Client("acme", acmeAddress));
        await fixture.ClientRepo.AddAsync(new Client("beta", betaAddress));

        var r1 = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);
        var r2 = await fixture.Management.IssueInvoiceAsync("beta", 200_00, new DateTime(2026, 1, 16), fixture.Exporter);

        Assert.That(r1.ExportSuccessful, Is.True);
        Assert.That(r2.ExportSuccessful, Is.True);
        Assert.That(r1.Invoice.Content.BuyerAddress.Name, Is.EqualTo("ACME EOOD"));
        Assert.That(r1.Invoice.Content.BuyerAddress.Address, Is.EqualTo("Acme St 1"));
        Assert.That(r2.Invoice.Content.BuyerAddress.Name, Is.EqualTo("Beta LLC"));
        Assert.That(r2.Invoice.Content.BuyerAddress.Address, Is.EqualTo("Beta Ave 2"));
    }

    [Test]
    public void IssueInvoiceAsync_GivenDateBeforeLastInvoice_WhenIssuing_ThenThrows()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var f = await CreateFixtureAsync();
            var client = new Client("acme", ValidBuyerAddress());
            await f.ClientRepo.AddAsync(client);
            await f.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 20), f.Exporter);
            await f.Management.IssueInvoiceAsync("acme", 200_00, new DateTime(2026, 1, 10), f.Exporter);
        });
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenExistingInvoice_WhenCorrecting_ThenUpdatesReExportsAndReturnsUpdatedInvoice()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);

        var result = await fixture.Management.CorrectInvoiceAsync(issueResult.Invoice.Number, 200_00, new DateTime(2026, 1, 20), fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.Number, Is.EqualTo(issueResult.Invoice.Number));
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(200_00));
        Assert.That(result.Invoice.Content.Date, Is.EqualTo(new DateTime(2026, 1, 20)));
        var stored = await fixture.InvoiceRepo.GetAsync(issueResult.Invoice.Number);
        Assert.That(stored.TotalAmount.Cents, Is.EqualTo(200_00));
    }

    [Test]
    public void CorrectInvoiceAsync_GivenNonExistingInvoice_WhenCorrecting_ThenThrows()
    {
        var fixture = CreateFixture();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Management.CorrectInvoiceAsync("99999", 100_00, null, fixture.Exporter));
    }

    [Test]
    public void CorrectInvoiceAsync_GivenInvalidDateChange_WhenCorrecting_ThenThrows()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var f = await CreateFixtureAsync();
            var client = new Client("acme", ValidBuyerAddress());
            await f.ClientRepo.AddAsync(client);
            await f.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), f.Exporter);
            await f.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 20), f.Exporter);
            // Second invoice is number 2. Try to correct it to have date before invoice 1
            await f.Management.CorrectInvoiceAsync("2", 100_00, new DateTime(2026, 1, 10), f.Exporter);
        });
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenAmountOnly_WhenCorrecting_ThenDateUnchanged()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var originalDate = new DateTime(2026, 2, 10);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 100_00, originalDate, fixture.Exporter);

        var result = await fixture.Management.CorrectInvoiceAsync(issueResult.Invoice.Number, 300_00, date: null, fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(300_00));
        Assert.That(result.Invoice.Content.Date, Is.EqualTo(originalDate));
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenDateOnly_WhenCorrecting_ThenAmountUnchanged()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 500_00, new DateTime(2026, 1, 15), fixture.Exporter);
        var newDate = new DateTime(2026, 2, 20);

        var result = await fixture.Management.CorrectInvoiceAsync(issueResult.Invoice.Number, amountCents: null, newDate, fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(500_00));
        Assert.That(result.Invoice.Content.Date, Is.EqualTo(newDate));
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenExistingInvoice_WhenCorrecting_ThenPreservesBuyerAddress()
    {
        var fixture = await CreateFixtureAsync();
        var buyerAddr = new BillingAddress(
            Name: "Unique Buyer Corp",
            RepresentativeName: "Jane",
            CompanyIdentifier: "999",
            VatIdentifier: null,
            Address: "Unique St 99",
            City: "Varna",
            PostalCode: "9000",
            Country: "BG");
        var client = new Client("acme", buyerAddr);
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);

        var result = await fixture.Management.CorrectInvoiceAsync(issueResult.Invoice.Number, 200_00, new DateTime(2026, 1, 20), fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.Content.BuyerAddress.Name, Is.EqualTo("Unique Buyer Corp"));
        Assert.That(result.Invoice.Content.BuyerAddress.Address, Is.EqualTo("Unique St 99"));
        Assert.That(result.Invoice.Content.BuyerAddress.City, Is.EqualTo("Varna"));
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenExistingInvoice_WhenCorrecting_ThenPdfReflectsUpdatedContent()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);
        Assert.That(issueResult.PdfPath, Is.Not.Null);
        Assert.That(File.ReadAllText(issueResult.PdfPath!), Does.Contain("amount 10000"));

        var result = await fixture.Management.CorrectInvoiceAsync(issueResult.Invoice.Number, 777_00, new DateTime(2026, 1, 20), fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(777_00));
        Assert.That(result.PdfPath, Is.Not.Null);
        var pdfContent = File.ReadAllText(result.PdfPath!);
        Assert.That(pdfContent, Does.Contain("amount 77700"));
    }

    [Test]
    public async Task ReExportInvoiceAsync_GivenExistingInvoice_WhenReExporting_ThenExportsPdfStoresInBlobStorageAndReturnsPath()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 250_00, new DateTime(2026, 1, 15), fixture.Exporter);

        var result = await fixture.Management.ReExportInvoiceAsync(issueResult.Invoice.Number, fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.Number, Is.EqualTo(issueResult.Invoice.Number));
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(250_00));
        Assert.That(result.PdfPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(result.PdfPath), Is.True);
        Assert.That(File.ReadAllText(result.PdfPath!), Does.Contain("invoice 1"));
    }

    [Test]
    public async Task ReExportInvoiceAsync_GivenCorrectedInvoice_WhenReExporting_ThenPdfReflectsCorrectedData()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);
        await fixture.Management.CorrectInvoiceAsync(issueResult.Invoice.Number, 999_00, new DateTime(2026, 1, 25), fixture.Exporter);

        var result = await fixture.Management.ReExportInvoiceAsync(issueResult.Invoice.Number, fixture.Exporter);

        Assert.That(result.ExportSuccessful, Is.True);
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(999_00));
        Assert.That(result.PdfPath, Is.Not.Null);
        Assert.That(File.ReadAllText(result.PdfPath!), Does.Contain("amount 99900"));
    }

    [Test]
    public void ReExportInvoiceAsync_GivenNonExistingInvoice_WhenReExporting_ThenThrows()
    {
        var fixture = CreateFixture();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Management.ReExportInvoiceAsync("99999", fixture.Exporter));
    }

    [Test]
    public async Task ReExportInvoiceAsync_GivenExistingInvoice_WhenReExporting_ThenOverwritesExistingPdf()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 100_00, null, fixture.Exporter);
        Assert.That(issueResult.PdfPath, Is.Not.Null);
        Assert.That(File.Exists(issueResult.PdfPath!), Is.True);

        var reExportResult = await fixture.Management.ReExportInvoiceAsync(issueResult.Invoice.Number, fixture.Exporter);

        Assert.That(reExportResult.ExportSuccessful, Is.True);
        Assert.That(reExportResult.PdfPath, Is.EqualTo(issueResult.PdfPath));
        Assert.That(File.Exists(reExportResult.PdfPath!), Is.True);
    }

    [Test]
    public async Task ListInvoicesAsync_GivenExistingInvoices_WhenListing_ThenReturnsLatestFirst()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);
        await fixture.Management.IssueInvoiceAsync("acme", 200_00, new DateTime(2026, 1, 20), fixture.Exporter);
        await fixture.Management.IssueInvoiceAsync("acme", 300_00, new DateTime(2026, 1, 25), fixture.Exporter);

        var result = await fixture.Management.ListInvoicesAsync(10);

        Assert.That(result.Items, Has.Count.EqualTo(3));
        Assert.That(result.Items[0].Number, Is.EqualTo("3"));
        Assert.That(result.Items[1].Number, Is.EqualTo("2"));
        Assert.That(result.Items[2].Number, Is.EqualTo("1"));
    }

    [Test]
    public async Task ListInvoicesAsync_GivenNoInvoices_WhenListing_ThenReturnsEmpty()
    {
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Management.ListInvoicesAsync(10);

        Assert.That(result.Items, Is.Empty);
        Assert.That(result.NextStartAfter, Is.Null);
    }

    [Test]
    public async Task IssueInvoiceAsync_WhenExporterThrows_ThenInvoiceCreatedButExportFailed()
    {
        var fixture = CreateFixtureWithExporter(new ThrowingExporter(), out var logger);
        await fixture.InitializeAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);

        var result = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);

        Assert.That(result.Invoice, Is.Not.Null);
        Assert.That(result.Invoice.Number, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(100_00));
        Assert.That(result.ExportSuccessful, Is.False);
        Assert.That(result.PdfPath, Is.Null);
        var stored = await fixture.InvoiceRepo.GetAsync(result.Invoice.Number);
        Assert.That(stored.Number, Is.EqualTo(result.Invoice.Number));
        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(1));
        Assert.That(logger.ErrorEntries[0].Message, Does.Contain("export failed"));
    }

    [Test]
    public async Task CorrectInvoiceAsync_WhenExporterThrows_ThenInvoiceCorrectedButExportFailed()
    {
        var fixture = CreateFixtureWithExporter(new ThrowingExporter(), out var logger);
        await fixture.InitializeAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);
        var originalNumber = issueResult.Invoice.Number;

        var result = await fixture.Management.CorrectInvoiceAsync(originalNumber, 200_00, new DateTime(2026, 1, 20), fixture.Exporter);

        Assert.That(result.Invoice.Number, Is.EqualTo(originalNumber));
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(200_00));
        Assert.That(result.ExportSuccessful, Is.False);
        Assert.That(result.PdfPath, Is.Null);
        var stored = await fixture.InvoiceRepo.GetAsync(originalNumber);
        Assert.That(stored.TotalAmount.Cents, Is.EqualTo(200_00));
        Assert.That(logger.ErrorEntries, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(logger.ErrorEntries.Any(e => e.Message.Contains("export failed")), Is.True);
    }

    [Test]
    public async Task ReExportInvoiceAsync_WhenExporterThrows_ThenReturnsInvoiceWithExportFailed()
    {
        var fixture = CreateFixtureWithExporter(new ThrowingExporter(), out var logger);
        await fixture.InitializeAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var issueResult = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);

        var result = await fixture.Management.ReExportInvoiceAsync(issueResult.Invoice.Number, fixture.Exporter);

        Assert.That(result.Invoice.Number, Is.EqualTo(issueResult.Invoice.Number));
        Assert.That(result.Invoice.TotalAmount.Cents, Is.EqualTo(100_00));
        Assert.That(result.ExportSuccessful, Is.False);
        Assert.That(result.PdfPath, Is.Null);
        Assert.That(logger.ErrorEntries, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(logger.ErrorEntries.Any(e => e.Message.Contains("export failed")), Is.True);
    }

    [Test]
    public async Task ListInvoicesAsync_GivenMoreInvoicesThanLimit_WhenListing_ThenRespectsLimit()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15), fixture.Exporter);
        await fixture.Management.IssueInvoiceAsync("acme", 200_00, new DateTime(2026, 1, 16), fixture.Exporter);
        await fixture.Management.IssueInvoiceAsync("acme", 300_00, new DateTime(2026, 1, 17), fixture.Exporter);
        await fixture.Management.IssueInvoiceAsync("acme", 400_00, new DateTime(2026, 1, 18), fixture.Exporter);
        await fixture.Management.IssueInvoiceAsync("acme", 500_00, new DateTime(2026, 1, 19), fixture.Exporter);

        var result = await fixture.Management.ListInvoicesAsync(2);

        Assert.That(result.Items, Has.Count.EqualTo(2));
        Assert.That(result.Items[0].Number, Is.EqualTo("5"));
        Assert.That(result.Items[1].Number, Is.EqualTo("4"));
        Assert.That(result.NextStartAfter, Is.Not.Null);
    }

    private static BillingAddress ValidBuyerAddress() => new(
        Name: "Test Buyer EOOD",
        RepresentativeName: "Мария Тестова",
        CompanyIdentifier: "444555666",
        VatIdentifier: null,
        Address: "ул. Проба 42",
        City: "Пловдив",
        PostalCode: "4000",
        Country: "BG");

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var f = CreateFixture();
        await f.InitializeAsync();
        return f;
    }

    private static TestFixture CreateFixture(IInvoiceExporter? exporter = null)
    {
        var exp = exporter ?? new FakeInvoiceExporter();
        var invoiceRepo = new FakeInvoiceRepo();
        var clientRepo = new FakeClientRepo();
        var tempDir = Path.Combine(Path.GetTempPath(), "InvoiceManagementTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var contentTypeMap = new Dictionary<string, string> { ["application/pdf"] = ".pdf" };
        var blobStorage = new LocalFileSystemBlobStorage(contentTypeMap);
        blobStorage.DefineBucket(InvoicesBucket, tempDir);
        var template = InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber()).GetAwaiter().GetResult();
        var management = new InvoiceManagement(
            invoiceRepo,
            clientRepo,
            template,
            blobStorage,
            SellerAddress,
            BankInfo,
            InvoicesBucket,
            new CapturingLogger(),
            LineItemDescription);
        return new TestFixture(invoiceRepo, clientRepo, management, exp, tempDir);
    }

    private static TestFixture CreateFixtureWithExporter(IInvoiceExporter exporter, out CapturingLogger logger)
    {
        logger = new CapturingLogger();
        var invoiceRepo = new FakeInvoiceRepo();
        var clientRepo = new FakeClientRepo();
        var tempDir = Path.Combine(Path.GetTempPath(), "InvoiceManagementTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var contentTypeMap = new Dictionary<string, string> { ["application/pdf"] = ".pdf" };
        var blobStorage = new LocalFileSystemBlobStorage(contentTypeMap);
        blobStorage.DefineBucket(InvoicesBucket, tempDir);
        var template = InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber()).GetAwaiter().GetResult();
        var management = new InvoiceManagement(
            invoiceRepo,
            clientRepo,
            template,
            blobStorage,
            SellerAddress,
            BankInfo,
            InvoicesBucket,
            logger,
            LineItemDescription);
        return new TestFixture(invoiceRepo, clientRepo, management, exporter, tempDir);
    }

    private sealed class TestFixture
    {
        public FakeInvoiceRepo InvoiceRepo { get; }
        public FakeClientRepo ClientRepo { get; }
        public InvoiceManagement Management { get; }
        public IInvoiceExporter Exporter { get; }
        private readonly string _tempDir;

        public TestFixture(
            FakeInvoiceRepo invoiceRepo,
            FakeClientRepo clientRepo,
            InvoiceManagement management,
            IInvoiceExporter exporter,
            string tempDir)
        {
            InvoiceRepo = invoiceRepo;
            ClientRepo = clientRepo;
            Management = management;
            Exporter = exporter;
            _tempDir = tempDir;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    /// Fake exporter that returns a minimal valid PDF stream without launching a browser.
    /// Writes invoice number and amount so tests can verify PDF content reflects invoice data.
    /// </summary>
    private sealed class FakeInvoiceExporter : IInvoiceExporter
    {
        public string MimeType => "application/pdf";

        public Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice)
        {
            var ms = new MemoryStream();
            var writer = new StreamWriter(ms);
            writer.Write("%PDF-1.4 fake content for invoice ");
            writer.Write(invoice.Number);
            writer.Write(" amount ");
            writer.Write(invoice.TotalAmount.Cents);
            writer.Flush();
            ms.Position = 0;
            return Task.FromResult<Stream>(ms);
        }
    }

    private sealed class ThrowingExporter : IInvoiceExporter
    {
        public string MimeType => "application/pdf";

        public Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice) =>
            throw new InvalidOperationException("Exporter failed");
    }
}
