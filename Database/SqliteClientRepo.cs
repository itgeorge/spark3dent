using Accounting;
using Invoices;
using Microsoft.EntityFrameworkCore;
using Utilities;

namespace Database;

public class SqliteClientRepo : IClientRepo
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteClientRepo(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Client?> FindByCompanyIdentifierAsync(string companyIdentifier)
    {
        await using var ctx = _contextFactory();

        var entity = await ctx.Clients
            .FirstOrDefaultAsync(c => c.CompanyIdentifier == companyIdentifier);

        return entity == null ? null : ClientMapping.ToDomain(entity);
    }

    public async Task<Client> GetAsync(string nickname)
    {
        await using var ctx = _contextFactory();

        var entity = await ctx.Clients.FindAsync(nickname)
            ?? throw new InvalidOperationException($"Client with nickname '{nickname}' not found.");

        return ClientMapping.ToDomain(entity);
    }

    public async Task<QueryResult<Client>> LatestAsync(int limit, string? startAfterCursor = null)
    {
        await using var ctx = _contextFactory();

        var lastDateByBuyerName = await ctx.Invoices
            .GroupBy(i => i.BuyerName)
            .Select(g => new { BuyerName = g.Key, LastDate = g.Max(i => i.Date) })
            .ToListAsync();

        var lastDateMap = lastDateByBuyerName
            .ToDictionary(x => x.BuyerName, x => x.LastDate, StringComparer.OrdinalIgnoreCase);

        var allClients = await ctx.Clients.ToListAsync();
        var clientsWithDate = allClients
            .Select(c => new { Entity = c, LastDate = lastDateMap.TryGetValue(c.Name, out var ld) ? ld : (DateTime?)null })
            .ToList();

        var ordered = clientsWithDate
            .OrderByDescending(x => x.LastDate ?? DateTime.MinValue)
            .ThenBy(x => x.Entity.Nickname, StringComparer.Ordinal)
            .AsEnumerable();

        if (!string.IsNullOrEmpty(startAfterCursor))
        {
            var parts = startAfterCursor.Split('|', 2);
            var cursorDate = parts.Length >= 1 && DateTime.TryParseExact(parts[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d)
                ? d
                : DateTime.MinValue;
            var cursorNick = parts.Length >= 2 ? parts[1] : "";
            ordered = ordered.Where(x =>
            {
                var dt = x.LastDate ?? DateTime.MinValue;
                return dt < cursorDate || (dt == cursorDate && string.Compare(x.Entity.Nickname, cursorNick, StringComparison.Ordinal) > 0);
            });
        }

        var pageEntities = ordered.Select(x => x.Entity).Take(limit).ToList();
        var items = pageEntities.Select(ClientMapping.ToDomain).ToList();
        var last = items.Count > 0 ? items[^1] : null;
        var lastEntity = last != null ? pageEntities[^1] : null;
        var nextStartAfter = lastEntity != null
            ? $"{(lastDateMap.TryGetValue(lastEntity.Name, out var ld) ? ld : DateTime.MinValue):yyyyMMdd}|{lastEntity.Nickname}"
            : null;

        return new QueryResult<Client>(items, nextStartAfter);
    }

    public async Task<QueryResult<Client>> ListAsync(int limit, string? startAfterCursor = null)
    {
        await using var ctx = _contextFactory();

        var query = ctx.Clients
            .OrderBy(c => c.Nickname)
            .AsQueryable();

        if (!string.IsNullOrEmpty(startAfterCursor))
        {
            var cursor = startAfterCursor;
            query = query.Where(c => c.Nickname.CompareTo(cursor) > 0);
        }

        var entities = await query.Take(limit).ToListAsync();
        var items = entities.Select(ClientMapping.ToDomain).ToList();
        var nextStartAfter = items.Count > 0 ? items[^1].Nickname : null;

        return new QueryResult<Client>(items, nextStartAfter);
    }

    public async Task AddAsync(Client client)
    {
        await using var ctx = _contextFactory();

        if (await ctx.Clients.AnyAsync(c => c.Nickname == client.Nickname))
            throw new InvalidOperationException($"Client with nickname '{client.Nickname}' already exists.");

        var entity = ClientMapping.ToEntity(client);
        ctx.Clients.Add(entity);
        await ctx.SaveChangesAsync();
    }

    public async Task UpdateAsync(string nickname, IClientRepo.ClientUpdate update)
    {
        await using var ctx = _contextFactory();

        await SqliteImmediateTransaction.ExecuteAsync(ctx, async c =>
        {
            var entity = await c.Clients.FindAsync(nickname)
                ?? throw new InvalidOperationException($"Client with nickname '{nickname}' not found.");

            var newNickname = update.Nickname ?? entity.Nickname;
            var newAddress = update.Address ?? new BillingAddress(
                entity.Name, entity.RepresentativeName, entity.CompanyIdentifier, entity.VatIdentifier,
                entity.Address, entity.City, entity.PostalCode, entity.Country);

            if (newNickname != nickname)
            {
                if (await c.Clients.AnyAsync(x => x.Nickname == newNickname))
                    throw new InvalidOperationException($"Client with nickname '{newNickname}' already exists.");
                c.Clients.Remove(entity);
                var newEntity = new Entities.ClientEntity
                {
                    Nickname = newNickname,
                    Name = newAddress.Name,
                    RepresentativeName = newAddress.RepresentativeName,
                    CompanyIdentifier = newAddress.CompanyIdentifier,
                    VatIdentifier = newAddress.VatIdentifier,
                    Address = newAddress.Address,
                    City = newAddress.City,
                    PostalCode = newAddress.PostalCode,
                    Country = newAddress.Country
                };
                c.Clients.Add(newEntity);
            }
            else
            {
                entity.Name = newAddress.Name;
                entity.RepresentativeName = newAddress.RepresentativeName;
                entity.CompanyIdentifier = newAddress.CompanyIdentifier;
                entity.VatIdentifier = newAddress.VatIdentifier;
                entity.Address = newAddress.Address;
                entity.City = newAddress.City;
                entity.PostalCode = newAddress.PostalCode;
                entity.Country = newAddress.Country;
            }

            await c.SaveChangesAsync();
        });
    }

    public async Task DeleteAsync(string nickname)
    {
        await using var ctx = _contextFactory();

        var entity = await ctx.Clients.FindAsync(nickname)
            ?? throw new InvalidOperationException($"Client with nickname '{nickname}' not found.");

        ctx.Clients.Remove(entity);
        await ctx.SaveChangesAsync();
    }
}
