using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Invoices;
using NUnit.Framework;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(InvoiceHtmlExporter))]
public class InvoiceHtmlExporterTest
{
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
    public void MimeType_ReturnsTextHtml()
    {
        var exporter = new InvoiceHtmlExporter();

        Assert.That(exporter.MimeType, Is.EqualTo("text/html"));
    }

    [Test]
    public async Task Export_WhenGivenValidInvoice_ReturnsNonEmptyStream()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var exporter = new InvoiceHtmlExporter();

        await using var stream = await exporter.Export(template, ValidInvoice);

        Assert.That(stream, Is.Not.Null);
        Assert.That(stream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Export_WhenGivenValidInvoice_ReturnsValidHtmlContent()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var exporter = new InvoiceHtmlExporter();

        await using var stream = await exporter.Export(template, ValidInvoice);
        stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = await reader.ReadToEndAsync();

        Assert.That(html, Does.Contain("<html").Or.Contain("<!DOCTYPE"));
    }

    [Test]
    public async Task Export_WhenGivenValidInvoice_ContainsAllInvoiceText()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        var exporter = new InvoiceHtmlExporter();

        await using var stream = await exporter.Export(template, ValidInvoice);
        stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = await reader.ReadToEndAsync();

        // Invoice number (padded to 10 digits)
        Assert.That(html, Does.Contain("0000000001"));
        // Date
        Assert.That(html, Does.Contain("2026-01-15"));
        // Seller
        Assert.That(html, Does.Contain("Test Seller EOOD"));
        Assert.That(html, Does.Contain("Иван Тестов"));
        Assert.That(html, Does.Contain("111222333"));
        Assert.That(html, Does.Contain("ул. Тестова 1"));
        Assert.That(html, Does.Contain("София"));
        // Buyer
        Assert.That(html, Does.Contain("Test Buyer EOOD"));
        Assert.That(html, Does.Contain("Мария Тестова"));
        Assert.That(html, Does.Contain("444555666"));
        Assert.That(html, Does.Contain("ул. Проба 42"));
        Assert.That(html, Does.Contain("Пловдив"));
        // Line item
        Assert.That(html, Does.Contain("Зъботехнически услуги"));
        Assert.That(html, Does.Contain("100.00"));
        // Bank transfer
        Assert.That(html, Does.Contain("BG00TEST12345678901234"));
        Assert.That(html, Does.Contain("Test Bank AD"));
        Assert.That(html, Does.Contain("TESTBGSF"));
    }
}
