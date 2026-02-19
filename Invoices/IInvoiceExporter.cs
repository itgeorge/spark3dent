namespace Invoices;

public interface IInvoiceExporter
{
    string MimeType { get; }
    Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice);
}