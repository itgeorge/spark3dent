using Invoices;
using Utilities;

namespace Accounting;

public class LoggingClientRepo : IClientRepo
{
    private readonly IClientRepo _inner;
    private readonly ILogger _logger;

    public LoggingClientRepo(IClientRepo inner, ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = new SafeLogger(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public async Task<Client> GetAsync(string nickname)
    {
        _logger.LogInfo($"ClientRepo.GetAsync nickname={nickname}");
        try
        {
            return await _inner.GetAsync(nickname);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ClientRepo.GetAsync nickname={nickname} failed", ex);
            throw;
        }
    }

    public async Task<QueryResult<Client>> ListAsync(int limit, string? startAfterCursor = null)
    {
        _logger.LogInfo($"ClientRepo.ListAsync limit={limit}, cursor={startAfterCursor ?? "(none)"}");
        try
        {
            return await _inner.ListAsync(limit, startAfterCursor);
        }
        catch (Exception ex)
        {
            _logger.LogError("ClientRepo.ListAsync failed", ex);
            throw;
        }
    }

    public async Task<QueryResult<Client>> LatestAsync(int limit, string? startAfterCursor = null)
    {
        _logger.LogInfo($"ClientRepo.LatestAsync limit={limit}, cursor={startAfterCursor ?? "(none)"}");
        try
        {
            return await _inner.LatestAsync(limit, startAfterCursor);
        }
        catch (Exception ex)
        {
            _logger.LogError("ClientRepo.LatestAsync failed", ex);
            throw;
        }
    }

    public async Task AddAsync(Client client)
    {
        _logger.LogInfo($"ClientRepo.AddAsync nickname={client.Nickname}");
        try
        {
            await _inner.AddAsync(client);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ClientRepo.AddAsync nickname={client.Nickname} failed", ex);
            throw;
        }
    }

    public async Task UpdateAsync(string nickname, IClientRepo.ClientUpdate update)
    {
        _logger.LogInfo($"ClientRepo.UpdateAsync nickname={nickname}");
        try
        {
            await _inner.UpdateAsync(nickname, update);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ClientRepo.UpdateAsync nickname={nickname} failed", ex);
            throw;
        }
    }

    public async Task DeleteAsync(string nickname)
    {
        _logger.LogInfo($"ClientRepo.DeleteAsync nickname={nickname}");
        try
        {
            await _inner.DeleteAsync(nickname);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ClientRepo.DeleteAsync nickname={nickname} failed", ex);
            throw;
        }
    }
}
