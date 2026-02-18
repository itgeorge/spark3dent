using System.Threading.Tasks;
using NUnit.Framework;

namespace Accounting.Tests.Fakes;

[TestFixture]
[TestOf(typeof(FakeClientRepo))]
public class FakeClientRepoTest : ClientRepoContractTest
{
    protected override Task<FixtureBase> SetUpAsync()
    {
        var repo = new FakeClientRepo();
        return Task.FromResult<FixtureBase>(new FakeFixture(repo));
    }

    private sealed class FakeFixture : FixtureBase
    {
        private readonly FakeClientRepo _repo;

        public FakeFixture(FakeClientRepo repo)
        {
            _repo = repo;
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
    }
}
