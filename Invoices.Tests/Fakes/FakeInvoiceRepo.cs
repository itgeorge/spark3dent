using System.Threading.Tasks;
using Utilities;

// TODO: might want this in a separate TestFakes project/ns in the future to not have to add dependencies to the .Tests
//  project here - but let's do that if it ever becomes a problem
namespace Invoices.Tests.Fakes;

public class FakeInvoiceRepo : IInvoiceRepo
{
    // TODO: implement as in-memory repo fake for tests, keep implementation simple, no optimizations. Support proper concurrent access to CreateAsync so that invoice numbering is correct, but don't worry about performance - locking the whole repo for every operation is fine, as long as it's simple and passes the ContractTest.
    
    public Task<Invoice> CreateAsync(Invoice.InvoiceContent content)
    {
        throw new System.NotImplementedException();
    }

    public Task<Invoice> GetAsync(string number)
    {
        throw new System.NotImplementedException();
    }

    public Task UpdateAsync(string number, Invoice.InvoiceContent content)
    {
        throw new System.NotImplementedException();
    }

    public Task<QueryResult<Invoice>> LatestAsync(int limit, string? startAfterCursor = null)
    {
        throw new System.NotImplementedException();
    }
}