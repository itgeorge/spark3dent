using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Invoices;
using NUnit.Framework;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(InvoiceHtmlTemplate))]
public class InvoiceHtmlTemplateTest
{
    private const string ValidTemplateHtml =
    """
    <html>
    <body>
    <h1 id="docTitle">Invoice</h1>
    <div id="sellerNameTop">Seller</div>
    <p>Invoice number: <span id="invNo">1234567890</span></p>
    <p>Invoice date: <span id="invDate">2026-01-01</span></p>
    <section>
        <p><strong id="sellerCompanyName">Seller Co</strong></p>
        <p>Address: <span id="sellerCity">City</span>, <span id="sellerAddr">Street 1</span></p>
        <p>MOL: <span id="sellerRepresentativeName">John Doe</span></p>
        <p>BULSTAT: <span id="sellerBulstat">123456789</span></p>
        <p>VAT: <span id="sellerVat">BG123456789</span></p>
    </section>
    <section>
        <p><strong id="buyerCompanyName">Buyer Co</strong></p>
        <p>Address: <span id="buyerAddr">City, Street 2</span></p>
        <p>MOL: <span id="buyerRepresentativeName">Jane Doe</span></p>
        <p>BULSTAT: <span id="buyerBulstat">987654321</span></p>
        <p>VAT: <span id="buyerVat">BG987654321</span></p>
    </section>
    <table>
        <tbody id="items">
        <tr><td data-field="idx" class="idx-class">1</td><td data-field="description" class="description-class">Item</td><td data-field="amount" class="amount-class">100.00 €</td></tr>
        </tbody>
    </table>
    <div id="totalWords">one hundred euro</div>
    <p>Place: <span id="placeOfSupply">Sofia</span></p>
    <p>Tax date: <span id="taxEventDate">2026-01-01</span></p>
    <p>Method: <span id="paymentMethod">Bank</span></p>
    <p>IBAN: <span id="iban">BG00BANK0000000000</span></p>
    <p>Bank: <span id="bank">Bank Name</span></p>
    <p>BIC: <span id="bic">BANKBG</span></p>
    <p>VAT basis: <span id="vatBasis">Art. 113</span></p>
    <div id="accountingNote">Accounting note</div>
    <p>Tax base: <strong id="taxBase">100.00 €</strong></p>
    <p>VAT 20%: <strong id="vat20">0.00 €</strong></p>
    <p>Total: <strong id="totalDue">100.00 €</strong></p>
    </body>
    </html>
    """;

    /// <summary>Test-specific template with unique markers to assert template override is used. Not shared with default.</summary>
    private const string TemplateUsesTemplateTestHtml =
    """
    <html data-test-identity="InvoiceHtmlTemplateTest-ThenUsesTemplate-unique-7f3a9b2e">
    <body>
    <div id="tpl-test-watermark">ZYXW-TEST-TEMPLATE-WATERMARK-9876</div>
    <h1 id="docTitle">Doc</h1>
    <div id="sellerNameTop">S</div>
    <span id="invNo">0</span>
    <span id="invDate">2026-01-01</span>
    <strong id="sellerCompanyName">S</strong>
    <span id="sellerCity">C</span><span id="sellerAddr">A</span>
    <span id="sellerRepresentativeName">R</span>
    <span id="sellerBulstat">E</span><span id="sellerVat">—</span>
    <strong id="buyerCompanyName">B</strong>
    <span id="buyerAddr">A</span>
    <span id="buyerRepresentativeName">R</span>
    <span id="buyerBulstat">E</span><span id="buyerVat">—</span>
    <div id="totalWords">w</div>
    <span id="placeOfSupply">P</span><span id="taxEventDate">D</span>
    <span id="paymentMethod">Bank</span>
    <span id="iban">BG00</span><span id="bank">Bank</span><span id="bic">BIC</span>
    <strong id="taxBase">0</strong><strong id="vat20">0</strong><strong id="totalDue">0</strong>
    <table><tbody id="items">
    <tr><td data-field="idx">1</td><td data-field="description">d</td><td data-field="amount">0</td></tr>
    </tbody></table>
    </body>
    </html>
    """;

    private static readonly BankTransferInfo TestBankTransferInfo = new(
        Iban: "BG00TEST12345678901234",
        BankName: "Test Bank AD",
        Bic: "TESTBGSF");

    private static readonly Invoice ValidInvoice = new(
        number: "1234567890",
        content: new Invoice.InvoiceContent(
            Date: new DateTime(2026, 01, 01),
            SellerAddress: new BillingAddress(
                Name: "СМТЛ Спарк 3Дент ООД",
                RepresentativeName: "Петя Бонева",
                CompanyIdentifier: "208300546",
                VatIdentifier: null,
                Address: "ул. Мирни дни 13, ет.6, ап.17",
                City: "Габрово",
                PostalCode: "5300",
                Country: "BG"),
            BuyerAddress: new BillingAddress(
                Name: "Георги Георгиев - Тест ООД",
                RepresentativeName: "Георги Георгиев",
                CompanyIdentifier: "456789012",
                VatIdentifier: null,
                Address: "ул. Хан Аспарух 9966, ет.6767, ап.987",
                City: "София",
                PostalCode: "1111",
                Country: "BG"),
            LineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(100_00, Currency.Eur)) },
            BankTransferInfo: TestBankTransferInfo
        ));

    private static readonly Invoice MultiLineItemInvoice = new(
        number: "1234567890",
        content: new Invoice.InvoiceContent(
            Date: new DateTime(2026, 11, 29),
            SellerAddress: new BillingAddress(
                Name: "Дентанематакова ЕООД",
                RepresentativeName: "Георги Петров",
                CompanyIdentifier: "987654321",
                VatIdentifier: null,
                Address: "ул. Васил Левски 252, ет.22, ап.4242",
                City: "Варна",
                PostalCode: "9000",
                Country: "BG"),
            BuyerAddress: new BillingAddress(
                Name: "ДВЕИДВЕСТА ООД",
                RepresentativeName: "Мария Николова",
                CompanyIdentifier: "123123123",
                VatIdentifier: null,
                Address: "ул. Александър Стамболийски 44477, ет.4, ап.123",
                City: "Пловдив",
                PostalCode: "4444",
                Country: "BG"),
            LineItems: new[]
            {
                new Invoice.LineItem("Зъболекарски консултации", new Amount(120_00, Currency.Eur)),
                new Invoice.LineItem("Профилактичен преглед", new Amount(80_00, Currency.Eur))
            },
            BankTransferInfo: TestBankTransferInfo
        ));

    private static readonly List<Invoice> ValidInvoices = new()
    {
        ValidInvoice,
        MultiLineItemInvoice,
    };

    private static readonly (string templateHtml, Invoice invoice)[] ValidTemplateAndInvoiceData = new[]
    {
        (ValidTemplateHtml, ValidInvoice),
        (ValidTemplateHtml, MultiLineItemInvoice),
    };

    private class FakeTranscriber : IAmountTranscriber
    {
        public bool Fail = false;

        public string Transcribe(Amount amount)
        {
            if (Fail)
            {
                throw new Exception("Transcriber failed");
            }
            return $"{amount.Cents} {amount.Currency}";
        }
    }

    [Test]
    [TestCaseSource(nameof(ValidTemplateAndInvoiceData))]
    public async Task Render_GivenValidTemplate_WhenRenderingInvoice_ThenAllFieldsPopulated((string templateHtml, Invoice invoice) data)
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber(), data.templateHtml);
        var html = template.Render(data.invoice);
        AssertFieldsPopulated(html, data.invoice);
    }

    [Test]
    public async Task Render_GivenValidTemplate_WhenRenderingInvoice_ThenUsesTemplate()
    {
        var tpl = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber(), TemplateUsesTemplateTestHtml);
        var html = tpl.Render(ValidInvoice);

        Assert.That(html, Does.Contain("ZYXW-TEST-TEMPLATE-WATERMARK-9876"),
            "Custom template contains unique watermark; proves override was used");
        Assert.That(html, Does.Contain("data-test-identity=\"InvoiceHtmlTemplateTest-ThenUsesTemplate-unique-7f3a9b2e\""),
            "Custom template has unique data attribute; default template would not");
    }

    [Test]
    [TestCaseSource(nameof(ValidInvoices))]
    public async Task Render_GivenDefaultTemplate_WhenRenderingInvoice_ThenAllFieldsPopulated(Invoice invoice)
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber());
        var html = template.Render(invoice);
        AssertFieldsPopulated(html, invoice);
    }

    [Test]
    [TestCase("idx")]
    [TestCase("description")]
    [TestCase("amount")]
    public void Render_GivenTemplateMissingLineItemField_WhenRenderingInvoice_ThenThrows(string missingLineItemFieldName)
    {
        var badTemplate = ValidTemplateHtml.Replace($"data-field=\"{missingLineItemFieldName}\"", "data-field=\"\"");
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber(), badTemplate));
        Assert.That(ex!.Message, Does.Contain("data-field").Or.Contain("template").Or.Contain(missingLineItemFieldName),
            "Exception should indicate expected template format");
    }

    [Test]
    public async Task Render_GivenValidTemplate_WhenRenderingInvoice_ThenCopiesTagAndClassAttributes()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber(), ValidTemplateHtml);
        var html = template.Render(MultiLineItemInvoice);
        var rows = GetLineItemRows(html);
        Assert.That(rows, Has.Length.EqualTo(2));
        var row1 = rows[0];
        Assert.That(row1.SelectSingleNode(".//td[@class='idx-class']"), Is.Not.Null);
        Assert.That(row1.SelectSingleNode(".//td[@class='description-class']"), Is.Not.Null);
        Assert.That(row1.SelectSingleNode(".//td[@class='amount-class']"), Is.Not.Null);
    }

    [Test]
    public void Render_GivenInvalidTemplate_WhenRenderingInvoice_ThenThrows()
    {
        var invalidHtml = "<html><body><div id=\"invNo\"></div></body></html>";
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber(), invalidHtml));
        Assert.That(ex!.Message, Does.Contain("template").Or.Contain("missing").Or.Contain("required"));
    }

    [Test]
    public async Task Render_GivenFailingTranscriber_WhenRenderingInvoice_ThenThrows()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber { Fail = true }, ValidTemplateHtml);
        Assert.Throws<Exception>(() => template.Render(ValidInvoice));
    }

    [Test]
    public async Task Render_GivenDefaultTemplate_WhenRenderingInvoiceWithNegativeAmount_ThenThrows()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber());
        var badInvoice = new Invoice("1", new Invoice.InvoiceContent(
            Date: DateTime.Today,
            SellerAddress: ValidInvoice.Content.SellerAddress,
            BuyerAddress: ValidInvoice.Content.BuyerAddress,
            LineItems: [new Invoice.LineItem("Bad", new Amount(-100, Currency.Eur))],
            BankTransferInfo: TestBankTransferInfo));
        var ex = Assert.Throws<ArgumentException>(() => template.Render(badInvoice));
        Assert.That(ex!.Message, Does.Contain("negative"));
    }

    [Test]
    public async Task Render_GivenDefaultPadding_WhenInvoiceNumberShort_ThenPaddedTo10Digits()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber(), ValidTemplateHtml);
        var shortNumberInvoice = new Invoice("4269", new Invoice.InvoiceContent(
            Date: new DateTime(2026, 1, 15),
            SellerAddress: ValidInvoice.Content.SellerAddress,
            BuyerAddress: ValidInvoice.Content.BuyerAddress,
            LineItems: [new Invoice.LineItem("Test", new Amount(100_00, Currency.Eur))],
            BankTransferInfo: TestBankTransferInfo));
        var html = template.Render(shortNumberInvoice);
        Assert.That(GetFieldValue(html, "invNo"), Is.EqualTo("0000004269"));
    }

    [Test]
    public async Task Render_GivenCustomPadding_WhenInvoiceNumberShort_ThenPaddedToSpecifiedLength()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber(), ValidTemplateHtml, invoiceNumberPadding: 5);
        var shortNumberInvoice = new Invoice("42", new Invoice.InvoiceContent(
            Date: new DateTime(2026, 1, 15),
            SellerAddress: ValidInvoice.Content.SellerAddress,
            BuyerAddress: ValidInvoice.Content.BuyerAddress,
            LineItems: [new Invoice.LineItem("Test", new Amount(50_00, Currency.Eur))],
            BankTransferInfo: TestBankTransferInfo));
        var html = template.Render(shortNumberInvoice);
        Assert.That(GetFieldValue(html, "invNo"), Is.EqualTo("00042"));
    }

    [Test]
    public async Task Render_GivenDefaultTemplate_WhenRenderingInvoiceWithZeroAmount_ThenAmountsAreZero()
    {
        var template = await InvoiceHtmlTemplate.LoadAsync(new FakeTranscriber());
        var zeroInvoice = new Invoice("1", new Invoice.InvoiceContent(
            Date: DateTime.Today,
            SellerAddress: ValidInvoice.Content.SellerAddress,
            BuyerAddress: ValidInvoice.Content.BuyerAddress,
            LineItems: [new Invoice.LineItem("Free", new Amount(0, Currency.Eur))],
            BankTransferInfo: TestBankTransferInfo));
        var html = template.Render(zeroInvoice);
        Assert.That(GetFieldValue(html, "taxBase"), Is.EqualTo("0.00 €"));
        Assert.That(GetFieldValue(html, "vat20"), Is.EqualTo("0.00 €"));
        Assert.That(GetFieldValue(html, "totalDue"), Is.EqualTo("0.00 €"));
    }

    private static void AssertFieldsPopulated(string html, Invoice invoice, int invoiceNumberPadding = 10)
    {
        var c = invoice.Content;
        var expectedInvNo = invoice.Number.Length >= invoiceNumberPadding
            ? invoice.Number
            : invoice.Number.PadLeft(invoiceNumberPadding, '0');
        Assert.That(GetFieldValue(html, "invNo"), Is.EqualTo(expectedInvNo));
        Assert.That(GetFieldValue(html, "invDate"), Is.EqualTo(c.Date.ToString("yyyy-MM-dd")));
        Assert.That(GetFieldValue(html, "sellerNameTop"), Is.EqualTo(c.SellerAddress.Name));
        Assert.That(GetFieldValue(html, "sellerCompanyName"), Is.EqualTo(c.SellerAddress.Name));
        Assert.That(GetFieldValue(html, "sellerCity"), Is.EqualTo(c.SellerAddress.City));
        Assert.That(GetFieldValue(html, "sellerAddr"), Is.EqualTo(c.SellerAddress.Address));
        Assert.That(GetFieldValue(html, "sellerRepresentativeName"), Is.EqualTo(c.SellerAddress.RepresentativeName));
        Assert.That(GetFieldValue(html, "sellerBulstat"), Is.EqualTo(c.SellerAddress.CompanyIdentifier));
        Assert.That(GetFieldValue(html, "buyerCompanyName"), Is.EqualTo(c.BuyerAddress.Name));
        Assert.That(GetFieldValue(html, "buyerRepresentativeName"), Is.EqualTo(c.BuyerAddress.RepresentativeName));
        Assert.That(GetFieldValue(html, "buyerBulstat"), Is.EqualTo(c.BuyerAddress.CompanyIdentifier));
        Assert.That(GetFieldValue(html, "paymentMethod"), Is.EqualTo("По сметка"));
        Assert.That(GetFieldValue(html, "iban"), Is.EqualTo(c.BankTransferInfo.Iban));
        Assert.That(GetFieldValue(html, "bank"), Is.EqualTo(c.BankTransferInfo.BankName));
        Assert.That(GetFieldValue(html, "bic"), Is.EqualTo(c.BankTransferInfo.Bic));
        Assert.That(GetFieldValue(html, "taxBase"), Does.Contain("€"));
        Assert.That(GetFieldValue(html, "vat20"), Does.Contain("€"));
        Assert.That(GetFieldValue(html, "totalDue"), Does.Contain("€"));

        var rows = GetLineItemRows(html);
        Assert.That(rows, Has.Length.EqualTo(c.LineItems.Length));
        for (var i = 0; i < c.LineItems.Length; i++)
        {
            Assert.That(GetCellValue(rows[i], "idx"), Is.EqualTo((i + 1).ToString()));
            Assert.That(GetCellValue(rows[i], "description"), Is.EqualTo(c.LineItems[i].Description));
            var expectedAmount = $"{c.LineItems[i].Amount.Cents / 100}.{c.LineItems[i].Amount.Cents % 100:D2} €";
            Assert.That(GetCellValue(rows[i], "amount"), Is.EqualTo(expectedAmount));
        }
    }

    private static string GetFieldValue(string html, string id)
    {
        var document = new HtmlDocument { OptionUseIdAttribute = true };
        document.LoadHtml(html);
        var element = document.GetElementbyId(id);
        return element?.InnerText.Trim() ?? throw new InvalidOperationException($"Field {id} not found in html");
    }

    private static HtmlNode[] GetLineItemRows(string html)
    {
        var document = new HtmlDocument { OptionUseIdAttribute = true };
        document.LoadHtml(html);
        var itemsTbody = document.GetElementbyId("items");
        if (itemsTbody == null)
            throw new InvalidOperationException("items tbody not found");
        var nodes = itemsTbody.SelectNodes(".//tr");
        if (nodes == null) return Array.Empty<HtmlNode>();
        return nodes.Cast<HtmlNode>().ToArray();
    }

    private static string GetCellValue(HtmlNode row, string dataField)
    {
        var cell = row.SelectSingleNode($".//*[@data-field='{dataField}']");
        return cell?.InnerText.Trim() ?? throw new InvalidOperationException($"data-field '{dataField}' not found in row");
    }
}