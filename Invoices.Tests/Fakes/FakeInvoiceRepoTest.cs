using System.Threading.Tasks;
using NUnit.Framework;

namespace Invoices.Tests.Fakes;

[TestFixture]
[TestOf(typeof(FakeInvoiceRepo))]
public class FakeInvoiceRepoTest : InvoiceRepoContractTest
{
    // TODO: implement fixture using fake repo to ensure fake behaves in line with contract
    
    protected override Task<FixtureBase> SetUpAsync()
    {
        throw new System.NotImplementedException();
    }
}