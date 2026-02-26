using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using Utilities;

namespace Invoices;

/// <summary>
/// Parses legacy invoice PDFs using PdfPig for region extraction and GPT for BillingAddress parsing.
/// Extracts recipient (Получател) data from a dynamic region between "Получател" and "Описание на стоката/услугата".
/// </summary>
public static class GptLegacyPdfParser
{
    private const string PoluchatelMarker = "Получател";
    private const string OpisanieMarker = "Описание";

    /// <summary>
    /// Attempts to parse a legacy invoice PDF, using GPT to extract the BillingAddress from the recipient region.
    /// Returns null if parsing fails.
    /// </summary>
    public static async Task<LegacyInvoiceData?> TryParseAsync(string pdfPath, string apiKey, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pdfPath))
            return null;

        try
        {
            var fullText = LegacyPdfParser.ExtractRawText(pdfPath);
            var (number, date, totalCents, currency) = LegacyPdfParser.ExtractMetadata(fullText);

            var regionText = ExtractTextFromRegion(pdfPath);
            var recipient = await ExtractBillingAddressWithGptAsync(regionText, apiKey, cancellationToken);

            if (number == null || !date.HasValue || !totalCents.HasValue || recipient == null)
                return null;

            return new LegacyInvoiceData(
                Number: number,
                Date: date.Value,
                TotalCents: totalCents.Value,
                Currency: currency,
                Recipient: recipient);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts text from the recipient region: from the top of "Получател" to the bottom of "Описание на стоката/услугата", full page width.
    /// </summary>
    public static string ExtractTextFromRegion(string pdfPath)
    {
        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var region = FindRecipientRegion(page);
        if (region is not { } r)
            return string.Empty;

        var words = page.GetWords()
            .Where(w => RectanglesIntersect(r, w.BoundingBox))
            .OrderBy(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .Select(w => w.Text);
        return string.Join(" ", words);
    }

    /// <summary>
    /// Finds the bounding box from the top of "Получател" to the bottom of "Описание" (Описание на стоката/услугата), full width.
    /// </summary>
    private static PdfRectangle? FindRecipientRegion(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToList();
        double? poluchatelTop = null;
        double? opisanieBottom = null;

        foreach (var w in words)
        {
            if (w.Text.Contains(PoluchatelMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (poluchatelTop == null || w.BoundingBox.Top > poluchatelTop)
                    poluchatelTop = w.BoundingBox.Top;
            }
            if (w.Text.Contains(OpisanieMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (opisanieBottom == null || w.BoundingBox.Bottom < opisanieBottom)
                    opisanieBottom = w.BoundingBox.Bottom;
            }
        }

        if (poluchatelTop == null || opisanieBottom == null || poluchatelTop <= opisanieBottom)
            return null;

        // Full page width: A4 = 595pt (standard invoice size)
        return new PdfRectangle(0, (short)opisanieBottom.Value, 595, (short)poluchatelTop.Value);
    }

    private static bool RectanglesIntersect(PdfRectangle a, PdfRectangle b)
    {
        return a.Left <= b.Right && a.Right >= b.Left && a.Bottom <= b.Top && a.Top >= b.Bottom;
    }

    private static async Task<BillingAddress?> ExtractBillingAddressWithGptAsync(string regionText, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(regionText))
            return null;

        var prompt = """
            Extract the billing/recipient (Получател) data from this Bulgarian invoice text. Return ONLY valid JSON with these exact keys:
            - "name" (company/person name)
            - "representativeName" (MOL - МОЛ). If not explicitly labeled, use a person name located after the EIK/ЕИК
            - "companyIdentifier" (EIK - ЕИК по Булстат)
            - "vatIdentifier" (optional, null if not present)
            - "address" (street address)
            - "city" (град)
            - "postalCode" (optional, empty string if not present)
            - "country" (default "България")

            Text:
            """ + regionText;

        var facade = new OpenAiFacade(apiKey);
        var response = await facade.Prompt(prompt, cancellationToken);

        return ParseBillingAddressFromJson(response);
    }

    private static BillingAddress? ParseBillingAddressFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var rep = root.TryGetProperty("representativeName", out var r) ? r.GetString() ?? "" : "";
            var eik = root.TryGetProperty("companyIdentifier", out var e) ? e.GetString() ?? "" : "";
            var vat = root.TryGetProperty("vatIdentifier", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var addr = root.TryGetProperty("address", out var a) ? a.GetString() ?? "" : "";
            var city = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
            var postal = root.TryGetProperty("postalCode", out var p) ? p.GetString() ?? "" : "";
            var country = root.TryGetProperty("country", out var co) ? co.GetString() ?? "България" : "България";

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(eik))
                return null;

            return new BillingAddress(
                Name: name.Trim(),
                RepresentativeName: rep.Trim(),
                CompanyIdentifier: eik.Trim(),
                VatIdentifier: string.IsNullOrWhiteSpace(vat) ? null : vat.Trim(),
                Address: addr.Trim(),
                City: city.Trim(),
                PostalCode: postal.Trim(),
                Country: country.Trim());
        }
        catch
        {
            return null;
        }
    }
}
