using System.Threading.Tasks;
using Utilities;

namespace Accounting.Tests.Fakes;

public class FakeClientRepo : IClientRepo
{
    // TODO: implement as in-memory repo fake for tests, keep implementation simple, no optimizations
    
    public Task<Client> GetAsync(string nickname)
    {
        throw new System.NotImplementedException();
    }

    public Task<QueryResult<Client>> ListAsync(int limit, string startAfterCursor = null)
    {
        throw new System.NotImplementedException();
    }

    public Task AddAsync(Client client)
    {
        throw new System.NotImplementedException();
    }

    public Task UpdateAsync(IClientRepo.ClientUpdate update)
    {
        throw new System.NotImplementedException();
    }

    public Task DeleteAsync(string nickname)
    {
        throw new System.NotImplementedException();
    }
}