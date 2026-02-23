using Invoices;
using Storage;
using Utilities;

namespace Accounting;

/// <summary>Export result: Uri may be a file path, cloud storage path, or data URI (e.g. data:image/png;base64,...).</summary>
public record ExportResult(bool Success, string? Uri);

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
    /// Creates a preview export for a future invoice with the next invoice number.
    /// Exports to image format only and returns a base64 data URI for use in HTML (e.g. img src).
    /// Works in both local and cloud environments without file system access.
    /// </summary>
    /// <param name="clientNickname">The client nickname.</param>
    /// <param name="amountCents">The amount in cents.</param>
    /// <param name="date">Optional invoice date; defaults to today.</param>
    /// <param name="imageExporter">The image exporter (e.g. image/png).</param>
    /// <returns>Preview result with base64 data URI, or failure.</returns>
    /// <exception cref="InvalidOperationException">When the client is not found.</exception>
    public async Task<ExportResult> PreviewInvoiceAsync(
        string clientNickname,
        int amountCents,
        DateTime? date,
        IInvoiceExporter imageExporter)
    {
        if (imageExporter.MimeType != "image/png" && imageExporter.MimeType != "image/jpeg")
            throw new ArgumentException("Preview requires an image exporter (image/png or image/jpeg).", nameof(imageExporter));

        var client = await _clientRepo.GetAsync(clientNickname);
        var invoiceDate = date ?? DateTime.UtcNow.Date;

        var latest = await _invoiceRepo.LatestAsync(1);
        var nextNumber = latest.Items.Count == 0
            ? "1"
            : (long.Parse(latest.Items[0].Number) + 1).ToString();

        var content = BuildInvoiceContent(invoiceDate, client.Address, amountCents);
        var invoice = new Invoice(nextNumber, content);

        try
        {
            await using var stream = await imageExporter.Export(_template, invoice);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            var dataUri = $"data:{imageExporter.MimeType};base64,{base64}";
            return new ExportResult(true, dataUri);
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
