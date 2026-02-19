using Utilities;

namespace Invoices;

public class LoggingInvoiceExporter : IInvoiceExporter
{
    public string MimeType => _inner.MimeType;

    private readonly IInvoiceExporter _inner;
    private readonly ILogger _logger;

    public LoggingInvoiceExporter(IInvoiceExporter inner, ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = new SafeLogger(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public async Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice)
    {
        _logger.LogInfo($"InvoiceExporter.Export invoiceNumber={invoice.Number}");
        try
        {
            var stream = await _inner.Export(template, invoice);
            _logger.LogInfo($"InvoiceExporter.Export completed invoiceNumber={invoice.Number}");
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError($"InvoiceExporter.Export invoiceNumber={invoice.Number} failed", ex);
            throw;
        }
    }
}
