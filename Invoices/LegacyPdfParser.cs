using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Invoices;

/// <summary>
/// Parses legacy manually-created PDF invoices to extract invoice number, date, total, currency, and recipient (Получател) data.
/// </summary>
public static class LegacyPdfParser
{
    /// <summary>
    /// Attempts to parse a legacy invoice PDF. Returns null if parsing fails.
    /// </summary>
    public static LegacyInvoiceData? TryParse(string pdfPath)
    {
        if (!File.Exists(pdfPath))
            return null;

        try
        {
            var text = ExtractRawText(pdfPath);
            return TryParseFromText(text);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to parse from raw extracted text. Used for unit testing.
    /// </summary>
    public static LegacyInvoiceData? TryParseFromText(string text)
    {
        var number = ExtractInvoiceNumber(text);
        var date = ExtractDate(text);
        var (totalCents, currency) = ExtractTotalAndCurrency(text);
        var recipient = ExtractRecipient(text);

        if (number == null || !date.HasValue || !totalCents.HasValue || recipient == null)
            return null;

        return new LegacyInvoiceData(
            Number: number,
            Date: date.Value,
            TotalCents: totalCents.Value,
            Currency: currency,
            Recipient: recipient);
    }

    /// <summary>
    /// Extracts metadata (number, date, total, currency) from full invoice text. Used by GptLegacyPdfParser.
    /// </summary>
    public static (string? Number, DateTime? Date, int? TotalCents, Currency Currency) ExtractMetadata(string text)
    {
        var number = ExtractInvoiceNumber(text);
        var date = ExtractDate(text);
        var (totalCents, currency) = ExtractTotalAndCurrency(text);
        return (number, date, totalCents, currency);
    }

    /// <summary>
    /// Extracts raw text from a PDF (for debugging/analysis).
    /// </summary>
    public static string ExtractRawText(string pdfPath)
    {
        using var doc = PdfDocument.Open(pdfPath);
        return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    private static string? ExtractInvoiceNumber(string text)
    {
        // Номер 0000000106 or Номер 14
        var m = Regex.Match(text, @"Номер\s+(\d+)");
        return m.Success ? m.Groups[1].Value.TrimStart('0').IfEmpty("0") : null;
    }

    private static DateTime? ExtractDate(string text)
    {
        // Дата 01.02.2024г. or 30.11.2021г. - search after "Дата" to avoid matching other dates (e.g. company registration)
        var dataIdx = text.IndexOf("Дата", StringComparison.OrdinalIgnoreCase);
        if (dataIdx < 0) return null;

        var afterData = text[dataIdx..];
        var m = Regex.Match(afterData, @"(\d{2}\.\d{2}\.\d{4})(?:г\.?|\s|$)"); // date followed by г., г, space, or end
        if (!m.Success) return null;
        if (DateTime.TryParseExact(m.Groups[1].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }

    private static (int? Cents, Currency Currency) ExtractTotalAndCurrency(string text)
    {
        // Сума за плащане: 270.00 лв. or 190.00 лв. or 320.00 евро - use last match (final total)
        var matches = Regex.Matches(text, @"Сума\s+за\s+плащане\s*:\s*([\d\s]+[.,]\d{2})\s*(лева|лв\.|евро)");
        if (matches.Count == 0) return (null, Currency.Bgn);
        var m = matches[^1];
        var amountStr = m.Groups[1].Value.Replace(" ", "").Replace(",", ".");
        if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            return (null, Currency.Bgn);
        var currency = m.Groups[2].Value.StartsWith("лв", StringComparison.OrdinalIgnoreCase) | m.Groups[2].Value.StartsWith("лева", StringComparison.OrdinalIgnoreCase)
            ? Currency.Bgn 
            : Currency.Eur;
        return ((int)Math.Round(amount * 100), currency);
    }

    private static BillingAddress? ExtractRecipient(string text)
    {
        // Получател ... : Адрес: ... ЕИК по Булстат: ... МОЛ: ...
        var poluchatelIdx = text.IndexOf("Получател", StringComparison.OrdinalIgnoreCase);
        if (poluchatelIdx < 0) return null;

        var afterPoluchatel = text[(poluchatelIdx + "Получател".Length)..];

        var name = ExtractRecipientName(afterPoluchatel);
        var (address, city) = ExtractAddress(afterPoluchatel);
        var eik = ExtractEik(afterPoluchatel);
        var mol = ExtractMol(afterPoluchatel);

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(eik))
            return null;

        return new BillingAddress(
            Name: name.Trim(),
            RepresentativeName: mol?.Trim() ?? "",
            CompanyIdentifier: eik.Trim(),
            VatIdentifier: null,
            Address: address?.Trim() ?? "",
            City: city?.Trim() ?? "",
            PostalCode: "",
            Country: "България");
    }

    private static string ExtractRecipientName(string afterPoluchatel)
    {
        // Company name is between start and ":" before "Адрес"
        var m = Regex.Match(afterPoluchatel, @"^(.+?)\s*:\s*Адрес", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success)
        {
            var name = m.Groups[1].Value.Trim();
            return string.IsNullOrWhiteSpace(name) ? "" : name;
        }
        var addrIdx = afterPoluchatel.IndexOf("Адрес", StringComparison.OrdinalIgnoreCase);
        if (addrIdx > 0)
        {
            var name = afterPoluchatel[..addrIdx].TrimEnd(':', ' ', '\t');
            return name;
        }
        return "";
    }

    private static (string? Address, string? City) ExtractAddress(string afterPoluchatel)
    {
        var m = Regex.Match(afterPoluchatel, @"Адрес\s*:\s*(.+?)(?=ЕИК|МОЛ|№|$)", RegexOptions.Singleline);
        if (!m.Success) return (null, null);
        var addrBlock = m.Groups[1].Value.Trim();
        // Format: гр.Килифарево, ул."Ал.Стамболийски"10 or ул.Юрий Венелин 22 гр.Габрово
        var cityMatch = Regex.Match(addrBlock, @"гр\.\s*([^,\s]+)");
        var city = cityMatch.Success ? cityMatch.Groups[1].Value : null;
        var addr = Regex.Replace(addrBlock, @"гр\.\s*[^,\s]+,?\s*", "").Trim();
        if (string.IsNullOrEmpty(addr)) addr = addrBlock;
        return (addr, city);
    }

    private static string? ExtractEik(string afterPoluchatel)
    {
        var m = Regex.Match(afterPoluchatel, @"ЕИК\s+по\s+Булстат\s*:\s*(\d+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? ExtractMol(string afterPoluchatel)
    {
        var m = Regex.Match(afterPoluchatel, @"МОЛ\s*:\s*([^№]+?)(?=№|ЕИК|Адрес|$)", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string IfEmpty(this string s, string defaultValue) =>
        string.IsNullOrWhiteSpace(s) ? defaultValue : s;
}

/// <summary>
/// Parsed data from a legacy PDF invoice.
/// </summary>
public record LegacyInvoiceData(
    string Number,
    DateTime Date,
    int TotalCents,
    Currency Currency,
    BillingAddress Recipient);
