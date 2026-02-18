using Invoices;
using Utilities;

namespace Accounting;

public interface IClientRepo
{
    public record ClientUpdate(string? Nickname, BillingAddress? Address);

    Task<Client> GetAsync(string nickname);
    Task<QueryResult<Client>> ListAsync(int limit, string? startAfterCursor = null);
    Task AddAsync(Client client);
    Task UpdateAsync(string nickname, ClientUpdate update);
    Task DeleteAsync(string nickname);
}