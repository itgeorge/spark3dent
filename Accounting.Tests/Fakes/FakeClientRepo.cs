using System;
using System.Collections.Generic;
using Accounting;
using System.Linq;
using System.Threading.Tasks;
using Invoices;
using Utilities;

namespace Accounting.Tests.Fakes;

public class FakeClientRepo : IClientRepo
{
    private readonly Dictionary<string, Client> _storage = new();
    private readonly object _lock = new();
    private readonly IInvoiceRepo? _invoiceRepo;

    public FakeClientRepo(IInvoiceRepo? invoiceRepo = null)
    {
        _invoiceRepo = invoiceRepo;
    }

    public Task<Client?> FindByCompanyIdentifierAsync(string companyIdentifier)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                return _storage.Values.FirstOrDefault(c => c.Address.CompanyIdentifier == companyIdentifier);
            }
        });
    }

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

    public async Task<QueryResult<Client>> LatestAsync(int limit, string? startAfterCursor = null)
    {
        if (_invoiceRepo == null)
            throw new NotImplementedException();

        var allInvoices = await GetAllInvoicesAsync();
        var lastDateByBuyerName = allInvoices
            .GroupBy(i => i.Content.BuyerAddress.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(i => i.Content.Date), StringComparer.OrdinalIgnoreCase);

        List<Client> clientsCopy;
        lock (_lock)
        {
            clientsCopy = _storage.Values.ToList();
        }

        var withDate = clientsCopy
            .Select(c => (Client: c, LastDate: lastDateByBuyerName.TryGetValue(c.Address.Name, out var ld) ? ld : (DateTime?)null))
            .OrderByDescending(x => x.LastDate ?? DateTime.MinValue)
            .ThenBy(x => x.Client.Nickname, StringComparer.Ordinal)
            .AsEnumerable();

        if (!string.IsNullOrEmpty(startAfterCursor))
        {
            var parts = startAfterCursor.Split('|', 2);
            var cursorDate = parts.Length >= 1 && DateTime.TryParseExact(parts[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d)
                ? d
                : DateTime.MinValue;
            var cursorNick = parts.Length >= 2 ? parts[1] : "";
            withDate = withDate.Where(x =>
            {
                var dt = x.LastDate ?? DateTime.MinValue;
                return dt < cursorDate || (dt == cursorDate && string.Compare(x.Client.Nickname, cursorNick, StringComparison.Ordinal) > 0);
            });
        }

        var items = withDate.Select(x => x.Client).Take(limit).ToList();
        var last = items.Count > 0 ? items[^1] : null;
        var nextStartAfter = last != null
            ? $"{(lastDateByBuyerName.TryGetValue(last.Address.Name, out var ld) ? ld : DateTime.MinValue):yyyyMMdd}|{last.Nickname}"
            : null;
        return new QueryResult<Client>(items, nextStartAfter);
    }

    private async Task<List<Invoice>> GetAllInvoicesAsync()
    {
        var list = new List<Invoice>();
        string? cursor = null;
        while (true)
        {
            var page = await _invoiceRepo!.LatestAsync(100, cursor);
            list.AddRange(page.Items);
            if (page.NextStartAfter == null) break;
            cursor = page.NextStartAfter;
        }
        return list;
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
                    if (_storage.ContainsKey(newNickname))
                        throw new InvalidOperationException($"Client with nickname '{newNickname}' already exists.");
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
