using System.Text;

namespace Invoices;

/// <summary>
/// Exports invoices as filled-in HTML by rendering the template with invoice data.
/// Returns the HTML string as a stream (no browser/PDF/image conversion).
/// </summary>
public class InvoiceHtmlExporter : IInvoiceExporter
{
    public string MimeType => "text/html";

    public Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice)
    {
        var html = template.Render(invoice);
        var bytes = Encoding.UTF8.GetBytes(html);
        var stream = new MemoryStream(bytes);
        return Task.FromResult<Stream>(stream);
    }
}
