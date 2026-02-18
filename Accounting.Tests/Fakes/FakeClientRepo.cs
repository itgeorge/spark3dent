using System;
using System.Collections.Generic;
using Accounting;
using System.Linq;
using System.Threading.Tasks;
using Utilities;

namespace Accounting.Tests.Fakes;

public class FakeClientRepo : IClientRepo
{
    private readonly Dictionary<string, Client> _storage = new();
    private readonly object _lock = new();

    public Task<Client> GetAsync(string nickname)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (!_storage.TryGetValue(nickname, out var client))
                    throw new InvalidOperationException($"Client with nickname '{nickname}' not found.");
                return client;
            }
        });
    }

    public Task<QueryResult<Client>> ListAsync(int limit, string? startAfterCursor = null)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                var ordered = _storage.Values
                    .OrderBy(c => c.Nickname, StringComparer.Ordinal)
                    .AsEnumerable();

                if (!string.IsNullOrEmpty(startAfterCursor))
                {
                    ordered = ordered.Where(c => string.Compare(c.Nickname, startAfterCursor, StringComparison.Ordinal) > 0);
                }

                var items = ordered.Take(limit).ToList();
                var nextStartAfter = items.Count > 0 ? items[^1].Nickname : null;

                return new QueryResult<Client>(items, nextStartAfter);
            }
        });
    }

    public Task AddAsync(Client client)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (_storage.ContainsKey(client.Nickname))
                    throw new InvalidOperationException($"Client with nickname '{client.Nickname}' already exists.");
                _storage[client.Nickname] = client;
            }
        });
    }

    public Task UpdateAsync(string nickname, IClientRepo.ClientUpdate update)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (!_storage.TryGetValue(nickname, out var existing))
                    throw new InvalidOperationException($"Client with nickname '{nickname}' not found.");

                var newNickname = update.Nickname ?? existing.Nickname;
                var newAddress = update.Address ?? existing.Address;
                var updated = new Client(newNickname, newAddress);

                if (newNickname != nickname)
                {
                    _storage.Remove(nickname);
                }
                _storage[newNickname] = updated;
            }
        });
    }

    public Task DeleteAsync(string nickname)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (!_storage.Remove(nickname))
                    throw new InvalidOperationException($"Client with nickname '{nickname}' not found.");
            }
        });
    }
}
