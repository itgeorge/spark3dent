namespace Invoices;

public class InvoicePdfExporter : IInvoiceExporter
{
    // TODO: add a regression test for this class (run it manually, review the result of several invoices, save the input and result as reference for the test assertions)

    public Task<Stream> Export(InvoiceHtmlTemplate template, Invoice invoice)
    {
        // TODO: Use a headless browser through Puppeteer or Playwright to render the html from rendering the template with the given data
        
        throw new NotImplementedException();
    }
}