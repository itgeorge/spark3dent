using System.Threading.Tasks;
using Accounting;
using NUnit.Framework;

namespace Accounting.Tests;

[TestFixture]
[TestOf(typeof(IClientRepo))]
public abstract class ClientRepoContractTest
{
    protected abstract Task<FixtureBase> SetUpAsync();

    protected abstract class FixtureBase
    {
        public abstract IClientRepo Repo { get; }
        public abstract Task SetUpClientAsync(Client client);
        public abstract Task<Client> GetClientAsync(string nickname);
    }

    // TODO: mimic tests from InvoiceRepoContractTest, without the date-sorting logic but covering add, get, update, delete operations with cases for missing/already existing records and list operations with proper paging
}