using System.Threading.Tasks;
using Invoices;
using NUnit.Framework;

namespace Invoices.Tests.Fakes;

[TestFixture]
[TestOf(typeof(FakeInvoiceRepo))]
public class FakeInvoiceRepoTest : InvoiceRepoContractTest
{
    [Test]
    public async Task PeekNextInvoiceNumber_GivenEmptyStorage_WhenPeeking_ThenReturnsStartInvoiceNumber()
    {
        var repo = new FakeInvoiceRepo(startInvoiceNumber: 1000);
        var next = await repo.PeekNextInvoiceNumberAsync();
        Assert.That(next, Is.EqualTo("1000"));
    }

    [Test]
    public async Task PeekNextInvoiceNumber_GivenExistingInvoices_WhenPeeking_ThenReturnsMaxPlusOne()
    {
        var repo = new FakeInvoiceRepo();
        var content = BuildValidInvoiceContent();
        await repo.CreateAsync(content);
        await repo.CreateAsync(BuildValidInvoiceContent(date: content.Date.AddDays(1)));

        var next = await repo.PeekNextInvoiceNumberAsync();
        Assert.That(next, Is.EqualTo("3"));
    }

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
