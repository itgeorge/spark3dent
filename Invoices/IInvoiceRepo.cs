using Utilities;

namespace Invoices;

public interface IInvoiceRepo
{
    Task<Invoice> CreateAsync(Invoice.InvoiceContent content);
    /// <summary>Imports an invoice with a specific number (legacy import). Does not advance the sequence. Throws if number already exists.</summary>
    Task<Invoice> ImportAsync(Invoice.InvoiceContent content, string number);
    Task<Invoice> GetAsync(string number);
    Task UpdateAsync(string number, Invoice.InvoiceContent content);
    Task<QueryResult<Invoice>> LatestAsync(int limit, string? startAfterCursor = null);
    /// <summary>Returns the next invoice number without creating an invoice. When no invoices exist, returns the configured start invoice number.</summary>
    Task<string> PeekNextInvoiceNumberAsync();
}