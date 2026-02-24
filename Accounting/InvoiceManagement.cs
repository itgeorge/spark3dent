using Invoices;
using Storage;
using Utilities;

namespace Accounting;

/// <summary>Export result: DataOrUri is whatever the exporter produces - may be an HTML or other string data, or if a uri: a file path, cloud storage path, or data URI (e.g. data:image/png;base64,...).</summary>
public record ExportResult(bool Success, string? DataOrUri);

public record InvoiceOperationResult(Invoice Invoice, ExportResult ExportResult);

public class InvoiceManagement
{
    private readonly IInvoiceRepo _invoiceRepo;
    private readonly IClientRepo _clientRepo;
    private readonly ILogger _logger;
    private readonly InvoiceHtmlTemplate _template;
    private readonly IBlobStorage _blobStorage;
    private readonly BillingAddress _sellerAddress;
    private readonly BankTransferInfo _bankTransferInfo;
    private readonly string _invoicesBucket;
    private readonly string _lineItemDescription;
    private readonly int _invoiceNumberPadding;

    public InvoiceManagement(
        IInvoiceRepo invoiceRepo,
        IClientRepo clientRepo,
        InvoiceHtmlTemplate template,
        IBlobStorage blobStorage,
        BillingAddress sellerAddress,
        BankTransferInfo bankTransferInfo,
        string invoicesBucket,
        ILogger logger,
        string lineItemDescription = "Зъботехнически услуги",
        int invoiceNumberPadding = 10)
    {
        _invoiceRepo = invoiceRepo ?? throw new ArgumentNullException(nameof(invoiceRepo));
        _clientRepo = clientRepo ?? throw new ArgumentNullException(nameof(clientRepo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _sellerAddress = sellerAddress ?? throw new ArgumentNullException(nameof(sellerAddress));
        _bankTransferInfo = bankTransferInfo ?? throw new ArgumentNullException(nameof(bankTransferInfo));
        _invoicesBucket = invoicesBucket ?? throw new ArgumentNullException(nameof(invoicesBucket));
        _lineItemDescription = lineItemDescription ?? throw new ArgumentNullException(nameof(lineItemDescription));
        _invoiceNumberPadding = invoiceNumberPadding;
    }

    public async Task<InvoiceOperationResult> IssueInvoiceAsync(
        string clientNickname,
        int amountCents,
        DateTime? date,
        IInvoiceExporter exporter)
    {
        var client = await _clientRepo.GetAsync(clientNickname);
        var invoiceDate = date ?? DateTime.UtcNow.Date;
        var content = BuildInvoiceContent(invoiceDate, client.Address, amountCents);

        var invoice = await _invoiceRepo.CreateAsync(content);
        var exportResult = await ExportAndStoreAsync(invoice, exporter);
        return ToInvoiceOperationResult(invoice, exportResult);
    }

    public async Task<InvoiceOperationResult> CorrectInvoiceAsync(
        string invoiceNumber,
        int? amountCents,
        DateTime? date,
        IInvoiceExporter exporter)
    {
        var existing = await _invoiceRepo.GetAsync(invoiceNumber);
        var newDate = date ?? existing.Content.Date;
        var newAmount = amountCents ?? existing.TotalAmount.Cents;
        var updatedContent = BuildInvoiceContent(newDate, existing.Content.BuyerAddress, newAmount);

        await _invoiceRepo.UpdateAsync(invoiceNumber, updatedContent);
        var updatedInvoice = new Invoice(invoiceNumber, updatedContent, isCorrected: true);
        var exportResult = await ExportAndStoreAsync(updatedInvoice, exporter);
        return ToInvoiceOperationResult(updatedInvoice, exportResult);
    }

    /// <summary>
    /// Re-exports an existing invoice and stores it in blob storage.
    /// Use when the template has changed or the export was lost/corrupted.
    /// </summary>
    /// <param name="invoiceNumber">The invoice number to re-export.</param>
    /// <param name="exporter">The exporter to use.</param>
    /// <returns>The invoice and export result.</returns>
    /// <exception cref="InvalidOperationException">When the invoice is not found.</exception>
    public async Task<InvoiceOperationResult> ReExportInvoiceAsync(
        string invoiceNumber,
        IInvoiceExporter exporter)
    {
        var invoice = await _invoiceRepo.GetAsync(invoiceNumber);
        var exportResult = await ExportAndStoreAsync(invoice, exporter);
        return ToInvoiceOperationResult(invoice, exportResult);
    }

    /// <summary>
    /// Creates a preview export for a future invoice with the next invoice number, or a specific number when correcting.
    /// Supports HTML (no Chromium) or image (PNG/JPEG, requires Chromium) exporters.
    /// </summary>
    /// <param name="clientNickname">The client nickname.</param>
    /// <param name="amountCents">The amount in cents.</param>
    /// <param name="date">Optional invoice date; defaults to today.</param>
    /// <param name="exporter">The exporter (e.g. InvoiceHtmlExporter, InvoiceImageExporter).</param>
    /// <param name="invoiceNumber">Optional invoice number for correction preview; when null or empty, uses the next number.</param>
    /// <returns>For HTML: ExportResult with Uri = raw HTML. For image: ExportResult with Uri = data:image/...;base64,...</returns>
    /// <exception cref="InvalidOperationException">When the client is not found.</exception>
    public async Task<ExportResult> PreviewInvoiceAsync(
        string clientNickname,
        int amountCents,
        DateTime? date,
        IInvoiceExporter exporter,
        string? invoiceNumber = null)
    {
        var client = await _clientRepo.GetAsync(clientNickname);
        var invoiceDate = date ?? DateTime.UtcNow.Date;

        string number;
        if (!string.IsNullOrWhiteSpace(invoiceNumber))
        {
            number = invoiceNumber.Trim();
        }
        else
        {
            var latest = await _invoiceRepo.LatestAsync(1);
            number = latest.Items.Count == 0
                ? "1"
                : (long.Parse(latest.Items[0].Number) + 1).ToString();
        }

        var content = BuildInvoiceContent(invoiceDate, client.Address, amountCents);
        var invoice = new Invoice(number, content);

        try
        {
            await using var stream = await exporter.Export(_template, invoice);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            if (exporter.MimeType == "text/html")
            {
                var html = System.Text.Encoding.UTF8.GetString(bytes);
                return new ExportResult(true, html);
            }
            if (exporter.MimeType == "image/png" || exporter.MimeType == "image/jpeg")
            {
                var base64 = Convert.ToBase64String(bytes);
                var dataUri = $"data:{exporter.MimeType};base64,{base64}";
                return new ExportResult(true, dataUri);
            }
            _logger.LogError("Preview export failed: unsupported MimeType", new ArgumentException($"Unsupported exporter MimeType: {exporter.MimeType}"));
            return new ExportResult(false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError("Invoice preview export failed", ex);
            return new ExportResult(false, null);
        }
    }

    public Task<QueryResult<Invoice>> ListInvoicesAsync(int limit)
    {
        return _invoiceRepo.LatestAsync(limit);
    }

    public Task<Invoice> GetInvoiceAsync(string number)
    {
        return _invoiceRepo.GetAsync(number);
    }

    /// <summary>
    /// Opens the PDF stream for an invoice. Throws FileNotFoundException if the PDF was not exported.
    /// </summary>
    public async Task<Stream> GetInvoicePdfStreamAsync(string number)
    {
        _ = await _invoiceRepo.GetAsync(number);
        var formattedNumber = number.Length >= _invoiceNumberPadding
            ? number
            : number.PadLeft(_invoiceNumberPadding, '0');
        var objectKey = $"invoice-{formattedNumber}";
        return await _blobStorage.OpenReadAsync(_invoicesBucket, objectKey);
    }

    private Invoice.InvoiceContent BuildInvoiceContent(DateTime date, BillingAddress buyerAddress, int amountCents)
    {
        var lineItems = new[]
        {
            new Invoice.LineItem(_lineItemDescription, new Amount(amountCents, Currency.Eur))
        };
        return new Invoice.InvoiceContent(
            Date: date,
            SellerAddress: _sellerAddress,
            BuyerAddress: buyerAddress,
            LineItems: lineItems,
            BankTransferInfo: _bankTransferInfo);
    }

    private async Task<ExportResult> ExportAndStoreAsync(Invoice invoice, IInvoiceExporter exporter)
    {
        try
        {
            await using var stream = await exporter.Export(_template, invoice);
            var formattedNumber = invoice.Number.Length >= _invoiceNumberPadding
                ? invoice.Number
                : invoice.Number.PadLeft(_invoiceNumberPadding, '0');
            var objectKey = $"invoice-{formattedNumber}";
            var uri = await _blobStorage.UploadAsync(_invoicesBucket, objectKey, stream, exporter.MimeType);
            return new ExportResult(true, uri);
        }
        catch (Exception ex)
        {
            _logger.LogError("Invoice export failed", ex);
            return new ExportResult(false, null);
        }
    }

    private static InvoiceOperationResult ToInvoiceOperationResult(Invoice invoice, ExportResult exportResult)
    {
        return new InvoiceOperationResult(invoice, exportResult);
    }
}
