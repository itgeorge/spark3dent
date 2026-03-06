using System.Reflection;
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
        "invNo", "invDate", "sellerCompanyName", "sellerCity", "sellerAddr",
        "sellerRepresentativeName", "sellerBulstat", "sellerVat", "buyerCompanyName", "buyerAddr",
        "buyerRepresentativeName", "buyerBulstat", "buyerVat", "totalWords", "taxBase", "vat20",
        "totalDue", "placeOfSupply", "taxEventDate", "paymentMethod", "iban", "bank", "bic"
    };

    private static readonly string[] RequiredLineItemFields = { "idx", "description", "amount" };

    private readonly HtmlDocument _templateDoc;
    private readonly IAmountTranscriber _amountTranscriber;
    private readonly int _invoiceNumberPadding;
    private readonly string _embeddedFontCss;

    private InvoiceHtmlTemplate(HtmlDocument templateDoc, IAmountTranscriber amountTranscriber, int invoiceNumberPadding, string embeddedFontCss)
    {
        _templateDoc = templateDoc;
        _amountTranscriber = amountTranscriber;
        _invoiceNumberPadding = invoiceNumberPadding;
        _embeddedFontCss = embeddedFontCss;
    }

    public static async Task<InvoiceHtmlTemplate> LoadAsync(IAmountTranscriber amountTranscriber, string? templateHtmlOverride = null, int invoiceNumberPadding = 10, string? logoBase64 = null)
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

        var assembly = typeof(InvoiceHtmlTemplate).Assembly;
        var embeddedFontCss = await BuildEmbeddedFontCssAsync(assembly);

        if (!string.IsNullOrWhiteSpace(logoBase64))
        {
            var logoContainer = doc.GetElementbyId("logoContainer");
            var logoImg = doc.GetElementbyId("logoImg");
            if (logoContainer != null && logoImg != null)
            {
                logoImg.SetAttributeValue("src", $"data:image/png;base64,{logoBase64.Trim()}");
                logoContainer.SetAttributeValue("style", "display: flex;");
            }
        }

        return new InvoiceHtmlTemplate(doc, amountTranscriber, invoiceNumberPadding, embeddedFontCss);
    }

    private static async Task<string> BuildEmbeddedFontCssAsync(Assembly assembly)
    {
        var interRegular = await EmbeddedResourceLoader.LoadEmbeddedResourceBytesAsync("Inter-Regular.woff2", assembly);
        var interSemiBold = await EmbeddedResourceLoader.LoadEmbeddedResourceBytesAsync("Inter-SemiBold.woff2", assembly);
        var interBold = await EmbeddedResourceLoader.LoadEmbeddedResourceBytesAsync("Inter-Bold.woff2", assembly);
        var interExtraBold = await EmbeddedResourceLoader.LoadEmbeddedResourceBytesAsync("Inter-ExtraBold.woff2", assembly);
        var interBlack = await EmbeddedResourceLoader.LoadEmbeddedResourceBytesAsync("Inter-Black.woff2", assembly);
        var cascadiaRegular = await EmbeddedResourceLoader.LoadEmbeddedResourceBytesAsync("CascadiaMono-Regular.woff2", assembly);
        var cascadiaSemiBold = await EmbeddedResourceLoader.LoadEmbeddedResourceBytesAsync("CascadiaMono-SemiBold.woff2", assembly);
        var cascadiaBold = await EmbeddedResourceLoader.LoadEmbeddedResourceBytesAsync("CascadiaMono-Bold.woff2", assembly);

        return BuildFontFaceCss(
            interRegular, interSemiBold, interBold, interExtraBold, interBlack,
            cascadiaRegular, cascadiaSemiBold, cascadiaBold);
    }

    private static string BuildFontFaceCss(
        byte[] interRegular, byte[] interSemiBold, byte[] interBold, byte[] interExtraBold, byte[] interBlack,
        byte[] cascadiaRegular, byte[] cascadiaSemiBold, byte[] cascadiaBold)
    {
        var r = Convert.ToBase64String(interRegular);
        var sb = Convert.ToBase64String(interSemiBold);
        var b = Convert.ToBase64String(interBold);
        var eb = Convert.ToBase64String(interExtraBold);
        var bl = Convert.ToBase64String(interBlack);
        var cr = Convert.ToBase64String(cascadiaRegular);
        var csb = Convert.ToBase64String(cascadiaSemiBold);
        var cb = Convert.ToBase64String(cascadiaBold);

        return $$"""
            @font-face{font-family:'Inter';src:url(data:font/woff2;base64,{{r}}) format('woff2');font-weight:400;font-style:normal}
            @font-face{font-family:'Inter';src:url(data:font/woff2;base64,{{sb}}) format('woff2');font-weight:600;font-style:normal}
            @font-face{font-family:'Inter';src:url(data:font/woff2;base64,{{b}}) format('woff2');font-weight:700;font-style:normal}
            @font-face{font-family:'Inter';src:url(data:font/woff2;base64,{{eb}}) format('woff2');font-weight:800;font-style:normal}
            @font-face{font-family:'Inter';src:url(data:font/woff2;base64,{{bl}}) format('woff2');font-weight:900;font-style:normal}
            @font-face{font-family:'Cascadia Mono';src:url(data:font/woff2;base64,{{cr}}) format('woff2');font-weight:400;font-style:normal}
            @font-face{font-family:'Cascadia Mono';src:url(data:font/woff2;base64,{{csb}}) format('woff2');font-weight:600;font-style:normal}
            @font-face{font-family:'Cascadia Mono';src:url(data:font/woff2;base64,{{cb}}) format('woff2');font-weight:700;font-style:normal}
            """;
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
        SetField(doc, "invDate", c.Date.ToString("dd.MM.yyyy 'г.'"));
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
        SetField(doc, "taxEventDate", c.Date.ToString("dd.MM.yyyy 'г.'"));
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

        var head = doc.DocumentNode.SelectSingleNode("//head");
        if (head != null)
        {
            var fontStyle = doc.CreateElement("style");
            fontStyle.InnerHtml = _embeddedFontCss;
            head.PrependChild(fontStyle);
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
