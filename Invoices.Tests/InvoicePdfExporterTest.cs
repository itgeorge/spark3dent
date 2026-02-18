using System;
using System.IO;
using System.Threading.Tasks;
using Invoices;
using NUnit.Framework;
using PuppeteerSharp;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(InvoicePdfExporter))]
public class InvoicePdfExporterTest
{
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var fetcher = new BrowserFetcher();
        await fetcher.DownloadAsync();
    }

    private static readonly BankTransferInfo TestBankTransferInfo = new(
        Iban: "BG00TEST12345678901234",
        BankName: "Test Bank AD",
        Bic: "TESTBGSF");

    private static readonly Invoice ValidInvoice = new(
        number: "1",
        content: new Invoice.InvoiceContent(
            Date: new DateTime(2026, 1, 15),
            SellerAddress: new BillingAddress(
                Name: "Test Seller EOOD",
                RepresentativeName: "Иван Тестов",
                CompanyIdentifier: "111222333",
                VatIdentifier: null,
                Address: "ул. Тестова 1",
                City: "София",
                PostalCode: "1000",
                Country: "BG"),
            BuyerAddress: new BillingAddress(
                Name: "Test Buyer EOOD",
                RepresentativeName: "Мария Тестова",
                CompanyIdentifier: "444555666",
                VatIdentifier: null,
                Address: "ул. Проба 42",
                City: "Пловдив",
                PostalCode: "4000",
                Country: "BG"),
            LineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(100_00, Currency.Eur)) },
            BankTransferInfo: TestBankTransferInfo));

    [Test]
    public async Task Export_WhenGivenValidInvoice_ReturnsNonEmptyStream()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var exporter = new InvoicePdfExporter();

        await using var stream = await exporter.Export(template, ValidInvoice);

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Export_WhenGivenValidInvoice_ReturnsValidPdfContent()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var exporter = new InvoicePdfExporter();

        await using var stream = await exporter.Export(template, ValidInvoice);

        var buffer = new byte[5];
        stream.Position = 0;
        var read = await stream.ReadAsync(buffer, 0, 5);

        Assert.That(read, Is.EqualTo(5));
        Assert.That(buffer[0], Is.EqualTo((byte)'%'));
        Assert.That(buffer[1], Is.EqualTo((byte)'P'));
        Assert.That(buffer[2], Is.EqualTo((byte)'D'));
        Assert.That(buffer[3], Is.EqualTo((byte)'F'));
    }
}
