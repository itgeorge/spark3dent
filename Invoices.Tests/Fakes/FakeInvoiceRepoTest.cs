using System.Threading.Tasks;
using NUnit.Framework;

namespace Invoices.Tests.Fakes;

[TestFixture]
[TestOf(typeof(FakeInvoiceRepo))]
public class FakeInvoiceRepoTest : InvoiceRepoContractTest
{
    protected override Task<FixtureBase> SetUpAsync()
    {
        var repo = new FakeInvoiceRepo();
        return Task.FromResult<FixtureBase>(new FakeFixture(repo));
    }

    private sealed class FakeFixture : FixtureBase
    {
        private readonly FakeInvoiceRepo _repo;

        public FakeFixture(FakeInvoiceRepo repo)
        {
            _repo = repo;
        }

        public override IInvoiceRepo Repo => _repo;

        public override Task<Invoice> SetUpInvoiceAsync(Invoice.InvoiceContent content)
        {
            return _repo.CreateAsync(content);
        }

        public override Task<Invoice> GetInvoiceAsync(string number)
        {
            return _repo.GetAsync(number);
        }
    }
}
