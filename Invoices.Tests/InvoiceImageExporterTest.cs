using System;
using System.Threading.Tasks;
using NUnit.Framework;
using PuppeteerSharp;
using SixLabors.ImageSharp;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(InvoiceImageExporter))]
public class InvoiceImageExporterTest
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
    public void MimeType_ReturnsImagePng()
    {
        var exporter = new InvoiceImageExporter();

        Assert.That(exporter.MimeType, Is.EqualTo("image/png"));
    }

    [Test]
    public async Task Export_WhenGivenValidInvoice_ReturnsNonEmptyStream()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var exporter = new InvoiceImageExporter();

        await using var stream = await exporter.Export(template, ValidInvoice);

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Export_WhenGivenValidInvoice_ReturnsValidPngContent()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var exporter = new InvoiceImageExporter();

        await using var stream = await exporter.Export(template, ValidInvoice);

        var buffer = new byte[8];
        stream.Position = 0;
        var read = await stream.ReadAsync(buffer, 0, 8);

        Assert.That(read, Is.EqualTo(8));
        // PNG magic bytes
        Assert.That(buffer[0], Is.EqualTo((byte)0x89));
        Assert.That(buffer[1], Is.EqualTo((byte)'P'));
        Assert.That(buffer[2], Is.EqualTo((byte)'N'));
        Assert.That(buffer[3], Is.EqualTo((byte)'G'));
        Assert.That(buffer[4], Is.EqualTo((byte)0x0D));
        Assert.That(buffer[5], Is.EqualTo((byte)0x0A));
        Assert.That(buffer[6], Is.EqualTo((byte)0x1A));
        Assert.That(buffer[7], Is.EqualTo((byte)0x0A));
    }

    [Test]
    public async Task Export_WhenGivenValidInvoice_ProducesDecodableImageWithReasonableDimensions()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var exporter = new InvoiceImageExporter();

        await using var stream = await exporter.Export(template, ValidInvoice);
        stream.Position = 0;

        // NOTE: ImageSharp is used here because it is the easiest library to use for loading PNGs.
        //  However, if we want to not depend on it due to license issues, we can drop this test or write a small png validator library.
        using var image = await Image.LoadAsync(stream);

        Assert.That(image.Width, Is.GreaterThan(100));
        Assert.That(image.Height, Is.GreaterThan(100));
    }
}
