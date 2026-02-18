using HtmlAgilityPack;
using Utilities;

namespace Invoices;

/// <summary>
/// Loads and renders the invoice HTML template. Uses HtmlAgilityPack (https://html-agility-pack.net/)
/// for HTML parsing and manipulation.
/// </summary>
public class InvoiceHtmlTemplate
{
    private const string TemplateResourceName = "template.html";
    private static readonly string[] RequiredElementIds =
    {
        "invNo", "invDate", "sellerNameTop", "sellerCompanyName", "sellerCity", "sellerAddr",
        "sellerRepresentativeName", "sellerBulstat", "sellerVat", "buyerCompanyName", "buyerAddr",
        "buyerRepresentativeName", "buyerBulstat", "buyerVat", "totalWords", "taxBase", "vat20",
        "totalDue", "placeOfSupply", "taxEventDate", "paymentMethod", "iban", "bank", "bic"
    };

    private static readonly string[] RequiredLineItemFields = { "idx", "description", "amount" };

    private readonly HtmlDocument _templateDoc;
    private readonly IAmountTranscriber _amountTranscriber;
    private readonly int _invoiceNumberPadding;

    private InvoiceHtmlTemplate(HtmlDocument templateDoc, IAmountTranscriber amountTranscriber, int invoiceNumberPadding)
    {
        _templateDoc = templateDoc;
        _amountTranscriber = amountTranscriber;
        _invoiceNumberPadding = invoiceNumberPadding;
    }

    public static async Task<InvoiceHtmlTemplate> LoadAsync(IAmountTranscriber amountTranscriber, string? templateHtmlOverride = null, int invoiceNumberPadding = 10)
    {
        var html = templateHtmlOverride ?? await EmbeddedResourceLoader.LoadEmbeddedResourceAsync(
            TemplateResourceName, typeof(InvoiceHtmlTemplate).Assembly);

        var doc = new HtmlDocument { OptionUseIdAttribute = true };
        try
        {
            doc.LoadHtml(html);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to parse template HTML. The template may be malformed.", ex);
        }

        foreach (var id in RequiredElementIds)
        {
            var el = doc.GetElementbyId(id);
            if (el == null)
                throw new InvalidOperationException($"Template is missing required element with id '{id}'.");
        }

        var itemsTbody = doc.GetElementbyId("items");
        if (itemsTbody == null)
            throw new InvalidOperationException("Template is missing required element with id 'items'.");

        var templateRow = itemsTbody.SelectSingleNode(".//tr");
        if (templateRow == null)
            throw new InvalidOperationException("Template #items tbody must contain at least one <tr> template row.");

        foreach (var field in RequiredLineItemFields)
        {
            var cell = templateRow.SelectSingleNode($".//*[@data-field='{field}']");
            if (cell == null)
                throw new InvalidOperationException(
                    $"Template line item row must have a cell with data-field='{field}'. Expected attributes: idx, description, amount.");
        }

        return new InvoiceHtmlTemplate(doc, amountTranscriber, invoiceNumberPadding);
    }

    public string Render(Invoice invoice)
    {
        var totalCents = invoice.TotalAmount.Cents;
        if (totalCents < 0)
            throw new ArgumentException("Invoice contains negative amount.", nameof(invoice));

        foreach (var li in invoice.Content.LineItems)
        {
            if (li.Amount.Cents < 0)
                throw new ArgumentException($"Line item '{li.Description}' has negative amount.", nameof(invoice));
        }

        string htmlCopy;
        using (var sw = new StringWriter())
        {
            _templateDoc.Save(sw);
            htmlCopy = sw.ToString();
        }
        var doc = new HtmlDocument { OptionUseIdAttribute = true };
        doc.LoadHtml(htmlCopy);
        var c = invoice.Content;
        var seller = c.SellerAddress;
        var buyer = c.BuyerAddress;

        var formattedNumber = invoice.Number.Length >= _invoiceNumberPadding
            ? invoice.Number
            : invoice.Number.PadLeft(_invoiceNumberPadding, '0');
        SetField(doc, "invNo", formattedNumber);
        SetField(doc, "invDate", c.Date.ToString("yyyy-MM-dd"));
        SetField(doc, "sellerNameTop", seller.Name);
        SetField(doc, "sellerCompanyName", seller.Name);
        SetField(doc, "sellerCity", seller.City);
        SetField(doc, "sellerAddr", seller.Address);
        SetField(doc, "sellerRepresentativeName", seller.RepresentativeName);
        SetField(doc, "sellerBulstat", seller.CompanyIdentifier);
        SetField(doc, "sellerVat", seller.VatIdentifier ?? "—");
        SetField(doc, "buyerCompanyName", buyer.Name);
        SetField(doc, "buyerAddr", $"{buyer.City}, {buyer.Address}");
        SetField(doc, "buyerRepresentativeName", buyer.RepresentativeName);
        SetField(doc, "buyerBulstat", buyer.CompanyIdentifier);
        SetField(doc, "buyerVat", buyer.VatIdentifier ?? "—");
        SetField(doc, "totalWords", _amountTranscriber.Transcribe(invoice.TotalAmount));
        SetField(doc, "placeOfSupply", seller.City);
        SetField(doc, "taxEventDate", c.Date.ToString("yyyy-MM-dd"));
        SetField(doc, "paymentMethod", "По сметка");
        SetField(doc, "iban", c.BankTransferInfo.Iban);
        SetField(doc, "bank", c.BankTransferInfo.BankName);
        SetField(doc, "bic", c.BankTransferInfo.Bic);

        var formattedTotal = FormatAmount(totalCents);
        SetField(doc, "taxBase", formattedTotal);
        SetField(doc, "vat20", "0.00 €");
        SetField(doc, "totalDue", formattedTotal);

        var itemsTbody = doc.GetElementbyId("items");
        var templateRow = itemsTbody!.SelectSingleNode(".//tr");
        if (templateRow == null)
            throw new InvalidOperationException("Template #items tbody has no row.");

        templateRow.Remove();
        for (var i = 0; i < c.LineItems.Length; i++)
        {
            var li = c.LineItems[i];
            var row = templateRow.CloneNode(true);
            SetDataField(row, "idx", (i + 1).ToString());
            SetDataField(row, "description", li.Description);
            SetDataField(row, "amount", FormatAmount(li.Amount.Cents));
            itemsTbody.AppendChild(row);
        }

        using var output = new StringWriter();
        doc.Save(output);
        return output.ToString();
    }

    private static void SetField(HtmlDocument doc, string id, string value)
    {
        var el = doc.GetElementbyId(id);
        if (el != null)
            el.InnerHtml = value;
    }

    private static void SetDataField(HtmlNode row, string dataField, string value)
    {
        var cell = row.SelectSingleNode($".//*[@data-field='{dataField}']");
        if (cell != null)
            cell.InnerHtml = value;
    }

    private static string FormatAmount(int cents)
    {
        var euros = cents / 100;
        var c = cents % 100;
        return $"{euros}.{c:D2} €";
    }
}
