using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Invoices;
using NUnit.Framework;
using PuppeteerSharp;

namespace Invoices.Tests;

/// <summary>
/// Regression tests for InvoicePdfExporter. Compares export output against pre-generated reference PDFs
/// in <c>ReferencePdfs/</c>. If rendering changes (template, PuppeteerSharp, Chromium), the test fails
/// so you can review and update the references.
/// </summary>
/// <remarks>
/// To regenerate references after an intentional change, set <c>REGENERATE_INVOICE_REFS=1</c> and run
/// the tests. The export output will be written to ReferencePdfs/; review the PDFs and commit.
/// </remarks>
[TestFixture]
[TestOf(typeof(InvoicePdfExporter))]
public class InvoicePdfExporterRegressionTest
{
    private static readonly BillingAddress ToolDefaultSeller = new(
        Name: "Dev Seller EOOD",
        RepresentativeName: "Иван Проба",
        CompanyIdentifier: "111222333",
        VatIdentifier: null,
        Address: "ул. Тестова 1, ет.1",
        City: "София",
        PostalCode: "1000",
        Country: "BG");

    private static readonly BillingAddress ToolDefaultBuyer = new(
        Name: "Dev Buyer EOOD",
        RepresentativeName: "Мария Проба",
        CompanyIdentifier: "444555666",
        VatIdentifier: null,
        Address: "ул. Проба 42, ап.5",
        City: "Пловдив",
        PostalCode: "4000",
        Country: "BG");

    private static readonly BankTransferInfo ToolDefaultBank = new(
        Iban: "BG03FINV91501017534825",
        BankName: "FIRST INVESTMENT BANK",
        Bic: "FINVBGSF");

    private InvoiceHtmlTemplate _template = null!;
    private InvoicePdfExporter _exporter = null!;
    private string _referencePdfsDir = null!;
    private bool _regenerateMode;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var fetcher = new BrowserFetcher();
        await fetcher.DownloadAsync();
        _template = await InvoiceHtmlTemplate.LoadAsync(new BgAmountTranscriber());
        _exporter = new InvoicePdfExporter();

        var testAssemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        // Use output/ReferencePdfs for comparison (Content copies project ReferencePdfs here)
        _referencePdfsDir = Path.GetFullPath(Path.Combine(testAssemblyDir, "ReferencePdfs"));
        _regenerateMode = Environment.GetEnvironmentVariable("REGENERATE_INVOICE_REFS") == "1";
    }

    [Test]
    public async Task Export_DefaultData_MatchesReference()
    {
        var invoice = CreateInvoice(
            number: "TPL-001",
            date: new DateTime(2026, 1, 15),
            seller: ToolDefaultSeller,
            buyer: ToolDefaultBuyer,
            bankTransferInfo: ToolDefaultBank,
            lineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(213_56, Currency.Eur)) });

        await AssertPdfMatchesReference(invoice, "default.pdf");
    }

    [Test]
    public async Task Export_CustomInvoiceNumberDateAmount_MatchesReference()
    {
        var invoice = CreateInvoice(
            number: "42",
            date: new DateTime(2026, 6, 30),
            seller: ToolDefaultSeller,
            buyer: ToolDefaultBuyer,
            bankTransferInfo: ToolDefaultBank,
            lineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(500_50, Currency.Eur)) });

        await AssertPdfMatchesReference(invoice, "custom-number-date-amount.pdf");
    }

    [Test]
    public async Task Export_CustomBuyerAddress_MatchesReference()
    {
        var customBuyer = new BillingAddress(
            Name: "Друга Фирма ООД",
            RepresentativeName: "Стоян Стоянов",
            CompanyIdentifier: "999888777",
            VatIdentifier: "BG999888777",
            Address: "бул. Витоша 100, офис 5",
            City: "Пловдив",
            PostalCode: "4000",
            Country: "BG");

        var invoice = CreateInvoice(
            number: "TPL-002",
            date: new DateTime(2026, 2, 1),
            seller: ToolDefaultSeller,
            buyer: customBuyer,
            bankTransferInfo: ToolDefaultBank,
            lineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(200_00, Currency.Eur)) });

        await AssertPdfMatchesReference(invoice, "custom-buyer.pdf");
    }

    [Test]
    public async Task Export_CustomSellerAddress_MatchesReference()
    {
        var customSeller = new BillingAddress(
            Name: "Дентал Лаб Варна ООД",
            RepresentativeName: "Иван Иванов",
            CompanyIdentifier: "123456789",
            VatIdentifier: "BG123456789",
            Address: "ул. Княз Борис 1 55",
            City: "Варна",
            PostalCode: "9000",
            Country: "BG");

        var invoice = CreateInvoice(
            number: "TPL-003",
            date: new DateTime(2026, 3, 15),
            seller: customSeller,
            buyer: ToolDefaultBuyer,
            bankTransferInfo: ToolDefaultBank,
            lineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(350_00, Currency.Eur)) });

        await AssertPdfMatchesReference(invoice, "custom-seller.pdf");
    }

    [Test]
    public async Task Export_CustomBankTransferInfo_MatchesReference()
    {
        var customBank = new BankTransferInfo(
            Iban: "BG80BNBG96611020345678",
            BankName: "БНБ - Българска народна банка",
            Bic: "BNBG");

        var invoice = CreateInvoice(
            number: "TPL-004",
            date: new DateTime(2026, 4, 20),
            seller: ToolDefaultSeller,
            buyer: ToolDefaultBuyer,
            bankTransferInfo: customBank,
            lineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(75_25, Currency.Eur)) });

        await AssertPdfMatchesReference(invoice, "custom-bank.pdf");
    }

    [Test]
    public async Task Export_MultipleLineItems_MatchesReference()
    {
        var invoice = CreateInvoice(
            number: "TPL-005",
            date: new DateTime(2026, 5, 10),
            seller: ToolDefaultSeller,
            buyer: ToolDefaultBuyer,
            bankTransferInfo: ToolDefaultBank,
            lineItems: new[]
            {
                new Invoice.LineItem("Зъболекарски консултации", new Amount(120_00, Currency.Eur)),
                new Invoice.LineItem("Профилактичен преглед", new Amount(80_00, Currency.Eur)),
                new Invoice.LineItem("Пломбиране", new Amount(50_00, Currency.Eur))
            });

        await AssertPdfMatchesReference(invoice, "multiple-line-items.pdf");
    }

    private static Invoice CreateInvoice(
        string number,
        DateTime date,
        BillingAddress seller,
        BillingAddress buyer,
        BankTransferInfo bankTransferInfo,
        Invoice.LineItem[] lineItems)
    {
        return new Invoice(number, new Invoice.InvoiceContent(
            Date: date,
            SellerAddress: seller,
            BuyerAddress: buyer,
            LineItems: lineItems,
            BankTransferInfo: bankTransferInfo));
    }

    private async Task AssertPdfMatchesReference(Invoice invoice, string referenceFileName)
    {
        await using var exportStream = await _exporter.Export(_template, invoice);
        using var ms = new MemoryStream();
        await exportStream.CopyToAsync(ms);
        var exportBytes = ms.ToArray();
        var exportNormalized = NormalizePdfTimestamps(exportBytes);

        var refPath = Path.Combine(_referencePdfsDir, referenceFileName);

        if (_regenerateMode)
        {
            // Write to project ReferencePdfs (not output) so it can be committed
            var projectRefDir = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "..", "..", "..", "ReferencePdfs"));
            Directory.CreateDirectory(projectRefDir);
            var projectRefPath = Path.Combine(projectRefDir, referenceFileName);
            await File.WriteAllBytesAsync(projectRefPath, exportBytes);
            var timestampPath = Path.Combine(projectRefDir, "generated.txt");
            await File.WriteAllTextAsync(timestampPath, DateTime.Now.ToString("yyyy-MM-dd:HHmmss"));
            Assert.Pass($"Regenerated reference: {projectRefPath}");
            return;
        }

        Assert.That(File.Exists(refPath), Is.True, $"Reference file not found: {refPath}");
        var refBytes = await File.ReadAllBytesAsync(refPath);
        var refNormalized = NormalizePdfTimestamps(refBytes);

        Assert.That(exportNormalized, Is.EqualTo(refNormalized),
            $"Exporter output does not match reference {referenceFileName}. " +
            "If the change is intentional (e.g. template update, PuppeteerSharp/Chromium upgrade), " +
            "set REGENERATE_INVOICE_REFS=1, run tests, review the new PDFs, and commit.");
    }

    /// <summary>Replaces PDF D:YYYYMMDDHHmmSS timestamps with fixed value so outputs can be compared.</summary>
    private static byte[] NormalizePdfTimestamps(byte[] pdf)
    {
        var result = (byte[])pdf.Clone();
        var placeholder = new byte[] { (byte)'2', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'1',
            (byte)'0', (byte)'1', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'0' };
        for (var i = 0; i < result.Length - 18; i++)
        {
            if (result[i] == 'D' && result[i + 1] == ':' && result[i + 2] == '2' && result[i + 3] == '0')
            {
                for (var j = 0; j < 14 && IsDigit(result[i + 4 + j]); j++)
                {
                    result[i + 4 + j] = j < placeholder.Length ? placeholder[j] : (byte)'0';
                }
            }
        }
        return result;
    }

    private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';
}
