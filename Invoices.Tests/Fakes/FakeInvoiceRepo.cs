using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Utilities;

namespace Invoices.Tests.Fakes;

public class FakeInvoiceRepo : IInvoiceRepo
{
    private readonly Dictionary<string, Invoice> _storage = new();
    private int _nextNumber;
    private readonly int _startInvoiceNumber;
    private readonly object _lock = new();

    public FakeInvoiceRepo(int startInvoiceNumber = 1)
    {
        _startInvoiceNumber = startInvoiceNumber;
        _nextNumber = startInvoiceNumber;
    }

    public Task<Invoice> ImportAsync(Invoice.InvoiceContent content, string number)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (_storage.ContainsKey(number))
                    throw new InvalidOperationException($"Invoice with number '{number}' already exists.");

                var invoice = new Invoice(number, content, isLegacy: true);
                _storage[number] = invoice;

                var importedNumeric = long.Parse(number);
                if (importedNumeric >= _nextNumber)
                    _nextNumber = (int)(importedNumeric + 1);

                return invoice;
            }
        });
    }

    public Task<Invoice> CreateAsync(Invoice.InvoiceContent content)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                var lastInvoice = _storage.Values
                    .OrderByDescending(i => long.Parse(i.Number))
                    .FirstOrDefault();

                if (lastInvoice != null && content.Date < lastInvoice.Content.Date)
                    throw new InvalidOperationException(
                        $"Invoice date {content.Date:yyyy-MM-dd} cannot be before the last invoice date {lastInvoice.Content.Date:yyyy-MM-dd}.");

                var number = _nextNumber.ToString();
                _nextNumber++;

                var invoice = new Invoice(number, content);
                _storage[number] = invoice;
                return invoice;
            }
        });
    }

    public Task<Invoice> GetAsync(string number)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (!_storage.TryGetValue(number, out var invoice))
                    throw new InvalidOperationException($"Invoice with number {number} not found.");
                return invoice;
            }
        });
    }

    public Task UpdateAsync(string number, Invoice.InvoiceContent content)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (!_storage.TryGetValue(number, out var existing))
                    throw new InvalidOperationException($"Invoice with number {number} not found.");

                if (existing.IsLegacy)
                    throw new InvalidOperationException($"Legacy invoice {number} cannot be edited.");

                var num = long.Parse(number);
                var prev = _storage.Values
                    .Where(i => long.Parse(i.Number) < num)
                    .OrderByDescending(i => long.Parse(i.Number))
                    .FirstOrDefault();
                var next = _storage.Values
                    .Where(i => long.Parse(i.Number) > num)
                    .OrderBy(i => long.Parse(i.Number))
                    .FirstOrDefault();

                if (prev != null && content.Date < prev.Content.Date)
                    throw new InvalidOperationException(
                        $"Invoice date {content.Date:yyyy-MM-dd} cannot be before the previous invoice date {prev.Content.Date:yyyy-MM-dd}.");

                if (next != null && content.Date > next.Content.Date)
                    throw new InvalidOperationException(
                        $"Invoice date {content.Date:yyyy-MM-dd} cannot be after the next invoice date {next.Content.Date:yyyy-MM-dd}.");

                _storage[number] = new Invoice(number, content, isCorrected: true);
            }
        });
    }

    public Task<QueryResult<Invoice>> LatestAsync(int limit, string? startAfterCursor = null)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                var ordered = _storage.Values
                    .OrderByDescending(i => long.Parse(i.Number))
                    .AsEnumerable();

                if (!string.IsNullOrEmpty(startAfterCursor))
                {
                    var cursorNum = long.Parse(startAfterCursor);
                    ordered = ordered.Where(i => long.Parse(i.Number) < cursorNum);
                }

                var items = ordered.Take(limit).ToList();
                var nextStartAfter = items.Count > 0 ? items[^1].Number : null;

                return new QueryResult<Invoice>(items, nextStartAfter);
            }
        });
    }

    public Task<string> PeekNextInvoiceNumberAsync()
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                var lastInvoice = _storage.Values
                    .OrderByDescending(i => long.Parse(i.Number))
                    .FirstOrDefault();
                return lastInvoice == null
                    ? _startInvoiceNumber.ToString()
                    : (long.Parse(lastInvoice.Number) + 1).ToString();
            }
        });
    }
}
