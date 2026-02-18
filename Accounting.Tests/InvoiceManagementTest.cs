using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Accounting;
using Accounting.Tests.Fakes;
using Invoices;
using Invoices.Tests.Fakes;
using NUnit.Framework;
using Storage;

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

        var (invoice, pdfPath) = await fixture.Management.IssueInvoiceAsync("acme", 150_00, null);

        Assert.That(invoice, Is.Not.Null);
        Assert.That(invoice.Number, Is.Not.Null.And.Not.Empty);
        Assert.That(invoice.TotalAmount.Cents, Is.EqualTo(150_00));
        Assert.That(invoice.Content.BuyerAddress.Name, Is.EqualTo("ACME EOOD"));
        Assert.That(invoice.Content.SellerAddress, Is.EqualTo(SellerAddress));
        Assert.That(invoice.Content.LineItems, Has.Length.EqualTo(1));
        Assert.That(invoice.Content.LineItems[0].Description, Is.EqualTo(LineItemDescription));
        Assert.That(pdfPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(pdfPath), Is.True);
        var stored = await fixture.InvoiceRepo.GetAsync(invoice.Number);
        Assert.That(stored.Number, Is.EqualTo(invoice.Number));
    }

    [Test]
    public void IssueInvoiceAsync_GivenNonExistingClient_WhenIssuing_ThenThrows()
    {
        var fixture = CreateFixture();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Management.IssueInvoiceAsync("nonexistent", 100_00, null));
    }

    [Test]
    public async Task IssueInvoiceAsync_GivenNullDate_WhenIssuing_ThenUsesToday()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);

        var (invoice, _) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, null);

        Assert.That(invoice.Content.Date.Date, Is.EqualTo(DateTime.UtcNow.Date));
    }

    [Test]
    public async Task IssueInvoiceAsync_GivenValidClient_WhenIssuing_ThenBankTransferInfoFromConfig()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);

        var (invoice, _) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, null);

        Assert.That(invoice.Content.BankTransferInfo, Is.EqualTo(BankInfo));
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

        var (inv1, _) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15));
        var (inv2, _) = await fixture.Management.IssueInvoiceAsync("beta", 200_00, new DateTime(2026, 1, 16));

        Assert.That(inv1.Content.BuyerAddress.Name, Is.EqualTo("ACME EOOD"));
        Assert.That(inv1.Content.BuyerAddress.Address, Is.EqualTo("Acme St 1"));
        Assert.That(inv2.Content.BuyerAddress.Name, Is.EqualTo("Beta LLC"));
        Assert.That(inv2.Content.BuyerAddress.Address, Is.EqualTo("Beta Ave 2"));
    }

    [Test]
    public void IssueInvoiceAsync_GivenDateBeforeLastInvoice_WhenIssuing_ThenThrows()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var f = await CreateFixtureAsync();
            var client = new Client("acme", ValidBuyerAddress());
            await f.ClientRepo.AddAsync(client);
            await f.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 20));
            await f.Management.IssueInvoiceAsync("acme", 200_00, new DateTime(2026, 1, 10));
        });
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenExistingInvoice_WhenCorrecting_ThenUpdatesReExportsAndReturnsUpdatedInvoice()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var (original, _) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15));

        var updated = await fixture.Management.CorrectInvoiceAsync(original.Number, 200_00, new DateTime(2026, 1, 20));

        Assert.That(updated.Number, Is.EqualTo(original.Number));
        Assert.That(updated.TotalAmount.Cents, Is.EqualTo(200_00));
        Assert.That(updated.Content.Date, Is.EqualTo(new DateTime(2026, 1, 20)));
        var stored = await fixture.InvoiceRepo.GetAsync(original.Number);
        Assert.That(stored.TotalAmount.Cents, Is.EqualTo(200_00));
    }

    [Test]
    public void CorrectInvoiceAsync_GivenNonExistingInvoice_WhenCorrecting_ThenThrows()
    {
        var fixture = CreateFixture();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Management.CorrectInvoiceAsync("99999", 100_00, null));
    }

    [Test]
    public void CorrectInvoiceAsync_GivenInvalidDateChange_WhenCorrecting_ThenThrows()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var f = await CreateFixtureAsync();
            var client = new Client("acme", ValidBuyerAddress());
            await f.ClientRepo.AddAsync(client);
            await f.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15));
            await f.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 20));
            // Second invoice is number 2. Try to correct it to have date before invoice 1
            await f.Management.CorrectInvoiceAsync("2", 100_00, new DateTime(2026, 1, 10));
        });
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenAmountOnly_WhenCorrecting_ThenDateUnchanged()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var originalDate = new DateTime(2026, 2, 10);
        var (original, _) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, originalDate);

        var updated = await fixture.Management.CorrectInvoiceAsync(original.Number, 300_00, date: null);

        Assert.That(updated.TotalAmount.Cents, Is.EqualTo(300_00));
        Assert.That(updated.Content.Date, Is.EqualTo(originalDate));
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenDateOnly_WhenCorrecting_ThenAmountUnchanged()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var (original, _) = await fixture.Management.IssueInvoiceAsync("acme", 500_00, new DateTime(2026, 1, 15));
        var newDate = new DateTime(2026, 2, 20);

        var updated = await fixture.Management.CorrectInvoiceAsync(original.Number, amountCents: null, newDate);

        Assert.That(updated.TotalAmount.Cents, Is.EqualTo(500_00));
        Assert.That(updated.Content.Date, Is.EqualTo(newDate));
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
        var (original, _) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15));

        var updated = await fixture.Management.CorrectInvoiceAsync(original.Number, 200_00, new DateTime(2026, 1, 20));

        Assert.That(updated.Content.BuyerAddress.Name, Is.EqualTo("Unique Buyer Corp"));
        Assert.That(updated.Content.BuyerAddress.Address, Is.EqualTo("Unique St 99"));
        Assert.That(updated.Content.BuyerAddress.City, Is.EqualTo("Varna"));
    }

    [Test]
    public async Task CorrectInvoiceAsync_GivenExistingInvoice_WhenCorrecting_ThenPdfReflectsUpdatedContent()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var (original, pdfPath) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15));
        Assert.That(File.ReadAllText(pdfPath), Does.Contain("amount 10000"));

        var updated = await fixture.Management.CorrectInvoiceAsync(original.Number, 777_00, new DateTime(2026, 1, 20));

        Assert.That(updated.TotalAmount.Cents, Is.EqualTo(777_00));
        var pdfContent = File.ReadAllText(pdfPath);
        Assert.That(pdfContent, Does.Contain("amount 77700"));
    }

    [Test]
    public async Task ReExportInvoiceAsync_GivenExistingInvoice_WhenReExporting_ThenExportsPdfStoresInBlobStorageAndReturnsPath()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var (invoice, _) = await fixture.Management.IssueInvoiceAsync("acme", 250_00, new DateTime(2026, 1, 15));

        var (reExported, pdfPath) = await fixture.Management.ReExportInvoiceAsync(invoice.Number);

        Assert.That(reExported.Number, Is.EqualTo(invoice.Number));
        Assert.That(reExported.TotalAmount.Cents, Is.EqualTo(250_00));
        Assert.That(pdfPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(pdfPath), Is.True);
        Assert.That(File.ReadAllText(pdfPath), Does.Contain("invoice 1"));
    }

    [Test]
    public async Task ReExportInvoiceAsync_GivenCorrectedInvoice_WhenReExporting_ThenPdfReflectsCorrectedData()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var (invoice, _) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15));
        await fixture.Management.CorrectInvoiceAsync(invoice.Number, 999_00, new DateTime(2026, 1, 25));

        var (reExported, pdfPath) = await fixture.Management.ReExportInvoiceAsync(invoice.Number);

        Assert.That(reExported.TotalAmount.Cents, Is.EqualTo(999_00));
        Assert.That(File.ReadAllText(pdfPath), Does.Contain("amount 99900"));
    }

    [Test]
    public void ReExportInvoiceAsync_GivenNonExistingInvoice_WhenReExporting_ThenThrows()
    {
        var fixture = CreateFixture();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Management.ReExportInvoiceAsync("99999"));
    }

    [Test]
    public async Task ReExportInvoiceAsync_GivenExistingInvoice_WhenReExporting_ThenOverwritesExistingPdf()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        var (invoice, firstPath) = await fixture.Management.IssueInvoiceAsync("acme", 100_00, null);
        Assert.That(File.Exists(firstPath), Is.True);

        var (_, secondPath) = await fixture.Management.ReExportInvoiceAsync(invoice.Number);

        Assert.That(secondPath, Is.EqualTo(firstPath));
        Assert.That(File.Exists(secondPath), Is.True);
    }

    [Test]
    public async Task ListInvoicesAsync_GivenExistingInvoices_WhenListing_ThenReturnsLatestFirst()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15));
        await fixture.Management.IssueInvoiceAsync("acme", 200_00, new DateTime(2026, 1, 20));
        await fixture.Management.IssueInvoiceAsync("acme", 300_00, new DateTime(2026, 1, 25));

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
    public async Task ListInvoicesAsync_GivenMoreInvoicesThanLimit_WhenListing_ThenRespectsLimit()
    {
        var fixture = await CreateFixtureAsync();
        var client = new Client("acme", ValidBuyerAddress());
        await fixture.ClientRepo.AddAsync(client);
        await fixture.Management.IssueInvoiceAsync("acme", 100_00, new DateTime(2026, 1, 15));
        await fixture.Management.IssueInvoiceAsync("acme", 200_00, new DateTime(2026, 1, 16));
        await fixture.Management.IssueInvoiceAsync("acme", 300_00, new DateTime(2026, 1, 17));
        await fixture.Management.IssueInvoiceAsync("acme", 400_00, new DateTime(2026, 1, 18));
        await fixture.Management.IssueInvoiceAsync("acme", 500_00, new DateTime(2026, 1, 19));

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

    private static TestFixture CreateFixture()
    {
        var invoiceRepo = new FakeInvoiceRepo();
        var clientRepo = new FakeClientRepo();
        var tempDir = Path.Combine(Path.GetTempPath(), "InvoiceManagementTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var contentTypeMap = new Dictionary<string, string> { ["application/pdf"] = ".pdf" };
        var blobStorage = new LocalFileSystemBlobStorage(contentTypeMap);
        blobStorage.DefineBucket(InvoicesBucket, tempDir);
        var fakeExporter = new FakeInvoiceExporter();
        var template = InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber()).GetAwaiter().GetResult();
        var management = new InvoiceManagement(
            invoiceRepo,
            clientRepo,
            fakeExporter,
            template,
            blobStorage,
            SellerAddress,
            BankInfo,
            InvoicesBucket,
            LineItemDescription);
        return new TestFixture(invoiceRepo, clientRepo, management, tempDir);
    }

    private sealed class TestFixture
    {
        public FakeInvoiceRepo InvoiceRepo { get; }
        public FakeClientRepo ClientRepo { get; }
        public InvoiceManagement Management { get; }
        private readonly string _tempDir;

        public TestFixture(FakeInvoiceRepo invoiceRepo, FakeClientRepo clientRepo, InvoiceManagement management, string tempDir)
        {
            InvoiceRepo = invoiceRepo;
            ClientRepo = clientRepo;
            Management = management;
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
}
