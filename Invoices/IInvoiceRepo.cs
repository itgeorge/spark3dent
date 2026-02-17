using Utilities;

namespace Invoices;

public interface IInvoiceRepo
{
    Task<Invoice> CreateAsync(Invoice.InvoiceContent content);
    Task<Invoice> GetAsync(string number);
    Task UpdateAsync(string number, Invoice.InvoiceContent content);
    Task<QueryResult<Invoice>> LatestAsync(int limit, string? startAfterCursor = null);
}