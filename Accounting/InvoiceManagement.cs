using Invoices;
using Storage;
using Utilities;

namespace Accounting;

public record InvoiceOperationResult(Invoice Invoice, bool ExportSuccessful, string? PdfPath);

public class InvoiceManagement
{
    /// <summary>MIME type used when storing invoice PDFs in blob storage.</summary>
    public const string PdfContentType = "application/pdf";

    private readonly IInvoiceRepo _invoiceRepo;
    private readonly IClientRepo _clientRepo;
    private readonly IInvoiceExporter _exporter;
    private readonly ILogger _logger;
    private readonly InvoiceHtmlTemplate _template;
    private readonly IBlobStorage _blobStorage;
    private readonly BillingAddress _sellerAddress;
    private readonly BankTransferInfo _bankTransferInfo;
    private readonly string _invoicesBucket;
    private readonly string _lineItemDescription;

    public InvoiceManagement(
        IInvoiceRepo invoiceRepo,
        IClientRepo clientRepo,
        IInvoiceExporter exporter,
        InvoiceHtmlTemplate template,
        IBlobStorage blobStorage,
        BillingAddress sellerAddress,
        BankTransferInfo bankTransferInfo,
        string invoicesBucket,
        ILogger logger,
        string lineItemDescription = "Зъботехнически услуги")
    {
        _invoiceRepo = invoiceRepo ?? throw new ArgumentNullException(nameof(invoiceRepo));
        _clientRepo = clientRepo ?? throw new ArgumentNullException(nameof(clientRepo));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _sellerAddress = sellerAddress ?? throw new ArgumentNullException(nameof(sellerAddress));
        _bankTransferInfo = bankTransferInfo ?? throw new ArgumentNullException(nameof(bankTransferInfo));
        _invoicesBucket = invoicesBucket ?? throw new ArgumentNullException(nameof(invoicesBucket));
        _lineItemDescription = lineItemDescription ?? throw new ArgumentNullException(nameof(lineItemDescription));
    }

    public async Task<InvoiceOperationResult> IssueInvoiceAsync(string clientNickname, int amountCents, DateTime? date)
    {
        var client = await _clientRepo.GetAsync(clientNickname);
        var invoiceDate = date ?? DateTime.UtcNow.Date;
        var content = BuildInvoiceContent(invoiceDate, client.Address, amountCents);

        var invoice = await _invoiceRepo.CreateAsync(content);
        var (exportSuccess, pdfPath) = await ExportAndStorePdfAsync(invoice);
        return new InvoiceOperationResult(invoice, exportSuccess, pdfPath);
    }

    public async Task<InvoiceOperationResult> CorrectInvoiceAsync(string invoiceNumber, int? amountCents, DateTime? date)
    {
        var existing = await _invoiceRepo.GetAsync(invoiceNumber);
        var newDate = date ?? existing.Content.Date;
        var newAmount = amountCents ?? existing.TotalAmount.Cents;
        var updatedContent = BuildInvoiceContent(newDate, existing.Content.BuyerAddress, newAmount);

        await _invoiceRepo.UpdateAsync(invoiceNumber, updatedContent);
        var updatedInvoice = new Invoice(invoiceNumber, updatedContent);
        var (exportSuccess, pdfPath) = await ExportAndStorePdfAsync(updatedInvoice);
        return new InvoiceOperationResult(updatedInvoice, exportSuccess, pdfPath);
    }

    /// <summary>
    /// Re-exports an existing invoice to PDF and stores it in blob storage.
    /// Use when the template has changed or the PDF was lost/corrupted.
    /// </summary>
    /// <param name="invoiceNumber">The invoice number to re-export.</param>
    /// <returns>The invoice and export result.</returns>
    /// <exception cref="InvalidOperationException">When the invoice is not found.</exception>
    public async Task<InvoiceOperationResult> ReExportInvoiceAsync(string invoiceNumber)
    {
        var invoice = await _invoiceRepo.GetAsync(invoiceNumber);
        var (exportSuccess, pdfPath) = await ExportAndStorePdfAsync(invoice);
        return new InvoiceOperationResult(invoice, exportSuccess, pdfPath);
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

    private async Task<(bool Success, string? Path)> ExportAndStorePdfAsync(Invoice invoice)
    {
        try
        {
            await using var pdfStream = await _exporter.Export(_template, invoice);
            var objectKey = $"invoice-{invoice.Number}";
            var path = await _blobStorage.UploadAsync(_invoicesBucket, objectKey, pdfStream, PdfContentType);
            return (true, path);
        }
        catch (Exception ex)
        {
            _logger.LogError("Invoice PDF export failed", ex);
            return (false, null);
        }
    }
}
