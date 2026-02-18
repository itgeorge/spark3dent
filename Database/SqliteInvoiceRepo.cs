using Configuration;
using Invoices;
using Microsoft.EntityFrameworkCore;
using Utilities;

namespace Database;

public class SqliteInvoiceRepo : IInvoiceRepo
{
    private const int SequenceId = 1;
    private readonly Func<AppDbContext> _contextFactory;
    private readonly Config _config;

    public SqliteInvoiceRepo(Func<AppDbContext> contextFactory, Config config)
    {
        _contextFactory = contextFactory;
        _config = config;
    }

    public async Task<Invoice> CreateAsync(Invoice.InvoiceContent content)
    {
        await using var ctx = _contextFactory();

        Invoice? result = null;
        await SqliteImmediateTransaction.ExecuteAsync(ctx, async c =>
        {
            await EnsureSequenceInitializedAsync(c);

            var seq = await c.InvoiceSequence.FindAsync(SequenceId)
                ?? throw new InvalidOperationException("Invoice sequence not initialized.");

            var lastInvoice = await c.Invoices
                .OrderByDescending(i => i.NumberNumeric)
                .FirstOrDefaultAsync();

            if (lastInvoice != null && content.Date < lastInvoice.Date)
            {
                throw new InvalidOperationException(
                    $"Invoice date {content.Date:yyyy-MM-dd} cannot be before the last invoice date {lastInvoice.Date:yyyy-MM-dd}.");
            }

            var nextNumber = (seq.LastNumber + 1).ToString();
            seq.LastNumber = seq.LastNumber + 1;

            var entity = InvoiceMapping.ToEntity(content);
            InvoiceMapping.SetNumber(entity, nextNumber);
            entity.Date = content.Date;

            c.Invoices.Add(entity);
            await c.SaveChangesAsync();
            result = InvoiceMapping.ToDomain(entity);
        });

        return result!;
    }

    public async Task<Invoice> GetAsync(string number)
    {
        await using var ctx = _contextFactory();

        var entity = await ctx.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Number == number)
            ?? throw new InvalidOperationException($"Invoice with number {number} not found.");

        return InvoiceMapping.ToDomain(entity);
    }

    public async Task UpdateAsync(string number, Invoice.InvoiceContent content)
    {
        await using var ctx = _contextFactory();

        await SqliteImmediateTransaction.ExecuteAsync(ctx, async c =>
        {
            var entity = await c.Invoices
                .Include(i => i.LineItems)
                .FirstOrDefaultAsync(i => i.Number == number)
                ?? throw new InvalidOperationException($"Invoice with number {number} not found.");

            var num = entity.NumberNumeric;
            var prevInvoice = await c.Invoices
                .Where(i => i.NumberNumeric < num)
                .OrderByDescending(i => i.NumberNumeric)
                .FirstOrDefaultAsync();
            var nextInvoice = await c.Invoices
                .Where(i => i.NumberNumeric > num)
                .OrderBy(i => i.NumberNumeric)
                .FirstOrDefaultAsync();

            if (prevInvoice != null && content.Date < prevInvoice.Date)
            {
                throw new InvalidOperationException(
                    $"Invoice date {content.Date:yyyy-MM-dd} cannot be before the previous invoice date {prevInvoice.Date:yyyy-MM-dd}.");
            }

            if (nextInvoice != null && content.Date > nextInvoice.Date)
            {
                throw new InvalidOperationException(
                    $"Invoice date {content.Date:yyyy-MM-dd} cannot be after the next invoice date {nextInvoice.Date:yyyy-MM-dd}.");
            }

            InvoiceMapping.ApplyContent(entity, content);
            await c.SaveChangesAsync();
        });
    }

    public async Task<QueryResult<Invoice>> LatestAsync(int limit, string? startAfterCursor = null)
    {
        await using var ctx = _contextFactory();

        var query = ctx.Invoices
            .Include(i => i.LineItems)
            .OrderByDescending(i => i.NumberNumeric)
            .AsQueryable();

        if (!string.IsNullOrEmpty(startAfterCursor))
        {
            var cursorNum = long.Parse(startAfterCursor);
            query = query.Where(i => i.NumberNumeric < cursorNum);
        }

        var entities = await query.Take(limit).ToListAsync();
        var items = entities.Select(InvoiceMapping.ToDomain).ToList();
        var nextStartAfter = items.Count > 0 ? items[^1].Number : null;

        return new QueryResult<Invoice>(items, nextStartAfter);
    }

    private async Task EnsureSequenceInitializedAsync(AppDbContext ctx)
    {
        var startNum = _config.App.StartInvoiceNumber - 1;
        await ctx.Database.ExecuteSqlRawAsync(
            "INSERT OR IGNORE INTO InvoiceSequence (Id, LastNumber) VALUES ({0}, {1})",
            SequenceId,
            startNum);
    }
}
