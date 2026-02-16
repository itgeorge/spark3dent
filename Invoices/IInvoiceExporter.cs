namespace Invoices;

public interface IInvoiceExporter
{
    public Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice);
}