using System.Threading.Tasks;
using Invoices;
using Invoices.Tests.Fakes;
using NUnit.Framework;

namespace Accounting.Tests.Fakes;

[TestFixture]
[TestOf(typeof(FakeClientRepo))]
public class FakeClientRepoTest : ClientRepoContractTest
{
    protected override Task<FixtureBase> SetUpAsync()
    {
        var invoiceRepo = new FakeInvoiceRepo();
        var clientRepo = new FakeClientRepo(invoiceRepo);
        return Task.FromResult<FixtureBase>(new FakeFixture(clientRepo, invoiceRepo));
    }

    private sealed class FakeFixture : FixtureBase
    {
        private readonly FakeClientRepo _repo;
        private readonly FakeInvoiceRepo _invoiceRepo;

        public FakeFixture(FakeClientRepo repo, FakeInvoiceRepo invoiceRepo)
        {
            _repo = repo;
            _invoiceRepo = invoiceRepo;
        }

        public override IClientRepo Repo => _repo;

        public override Task SetUpClientAsync(Client client)
        {
            return _repo.AddAsync(client);
        }

        public override Task<Client> GetClientAsync(string nickname)
        {
            return _repo.GetAsync(nickname);
        }

        public override Task<Invoice> SetUpInvoiceAsync(Invoice.InvoiceContent content)
        {
            return _invoiceRepo.CreateAsync(content);
        }
    }
}
