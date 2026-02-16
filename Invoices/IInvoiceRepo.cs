using Utilities;

namespace Invoices;

public interface IInvoiceRepo
{
    Task CreateAsync(Invoice invoice);
    Task<Invoice> GetAsync(string number);
    Task UpdateAsync(string number, Invoice.InvoiceContent content);
    Task<QueryResult<Invoice>> LatestAsync(int limit, string? startAfterCursor = null);
}