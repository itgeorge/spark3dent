using System;
using System.Collections.Generic;
//using HtmlAgilityPack; TODO: uncomment this when HtmlAgilityPack is added to the project
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
            LineItems: new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(100_00, Currency.Eur)) }
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
            }
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
    public void Render_GivenValidTemplate_WhenRenderingInvoice_ThenAllFieldsPopulated((string templateHtml, Invoice invoice) data)
    {
        // TODO: use data.invoice and use data.templateHtml, render the invoice and check all fields are populated (nake sure to loop over all line items and check all fields of each line item are populated)
    }

    [Test]
    [TestCaseSource(nameof(ValidInvoices))]
    public void Render_GivenDefaultTemplate_WhenRenderingInvoice_ThenAllFieldsPopulated(Invoice invoice)
    {
        // TODO: use invoice and don't pass template override, render the invoice and check all fields are populated (make sure to loop over all line items and check all fields of each line item are populated)
    }

    [Test]
    [TestCase("idx")]
    [TestCase("description")]
    [TestCase("amount")]
    public void Render_GivenTemplateMissingLineItemField_WhenRenderingInvoice_ThenThrows(string missingLineItemFieldName)
    {
        // TODO: use the valid template, but replace the data-field attribute with an empty, assert exception thrown with message indicating expected template format
    }

    [Test]
    public void Render_GivenValidTemplate_WhenRenderingInvoice_ThenCopiesTagAndClassAttributes()
    {
        // TODO: use the valid template, render the multi-line item invoice and check the tag and class attributes are copied to the output html for each line item
    }

    [Test]
    public void Render_GivenInvalidTemplate_WhenRenderingInvoice_ThenThrows()
    {
        // TODO: use an invalid template, render the invoice and check it throws
    }

    [Test]
    public void Render_GivenFailingTranscriber_WhenRenderingInvoice_ThenThrows()
    {
        // TODO: use a failing transcriber, render the invoice and check it throws
    }

    [Test]
    public void Render_GivenDefaultTemplate_WhenRenderingInvoiceWithNegativeAmount_ThenThrows()
    {
        // TODO: create an invoice with a negative amount, render the invoice and check it throws
    }

    [Test]
    public void Render_GivenDefaultTemplate_WhenRenderingInvoiceWithZeroAmount_ThenAmountsAreZero()
    {
        // TODO: create an invoice with a zero total amount, render the invoice and check the amounts are zero
    }

    private static string GetFieldValue(string html, string id)
    {
        // TODO: uncomment this when HtmlAgilityPack is added to the project (edit code here if needed)
        /* var document = new HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(html);
        return document.GetElementById(id)?.InnerText ?? throw new InvalidOperationException($"Field {id} not found in html"); */
        throw new NotImplementedException();
    }
}