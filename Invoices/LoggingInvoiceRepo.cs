using Utilities;

namespace Invoices;

public class LoggingInvoiceRepo : IInvoiceRepo
{
    private readonly IInvoiceRepo _inner;
    private readonly ILogger _logger;

    public LoggingInvoiceRepo(IInvoiceRepo inner, ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = new SafeLogger(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public async Task<Invoice> CreateAsync(Invoice.InvoiceContent content)
    {
        _logger.LogInfo($"InvoiceRepo.CreateAsync");
        try
        {
            var invoice = await _inner.CreateAsync(content);
            _logger.LogInfo($"InvoiceRepo.CreateAsync completed, number={invoice.Number}");
            return invoice;
        }
        catch (Exception ex)
        {
            _logger.LogError("InvoiceRepo.CreateAsync failed", ex);
            throw;
        }
    }

    public async Task<Invoice> ImportAsync(Invoice.InvoiceContent content, string number)
    {
        _logger.LogInfo($"InvoiceRepo.ImportAsync number={number}");
        try
        {
            var invoice = await _inner.ImportAsync(content, number);
            _logger.LogInfo($"InvoiceRepo.ImportAsync completed, number={invoice.Number}");
            return invoice;
        }
        catch (Exception ex)
        {
            _logger.LogError($"InvoiceRepo.ImportAsync number={number} failed", ex);
            throw;
        }
    }

    public async Task<Invoice> GetAsync(string number)
    {
        _logger.LogInfo($"InvoiceRepo.GetAsync number={number}");
        try
        {
            var invoice = await _inner.GetAsync(number);
            return invoice;
        }
        catch (Exception ex)
        {
            _logger.LogError($"InvoiceRepo.GetAsync number={number} failed", ex);
            throw;
        }
    }

    public async Task UpdateAsync(string number, Invoice.InvoiceContent content)
    {
        _logger.LogInfo($"InvoiceRepo.UpdateAsync number={number}");
        try
        {
            await _inner.UpdateAsync(number, content);
        }
        catch (Exception ex)
        {
            _logger.LogError($"InvoiceRepo.UpdateAsync number={number} failed", ex);
            throw;
        }
    }

    public async Task<string> PeekNextInvoiceNumberAsync()
    {
        _logger.LogInfo("InvoiceRepo.PeekNextInvoiceNumberAsync");
        try
        {
            var number = await _inner.PeekNextInvoiceNumberAsync();
            _logger.LogInfo($"InvoiceRepo.PeekNextInvoiceNumberAsync completed, number={number}");
            return number;
        }
        catch (Exception ex)
        {
            _logger.LogError("InvoiceRepo.PeekNextInvoiceNumberAsync failed", ex);
            throw;
        }
    }

    public async Task<QueryResult<Invoice>> LatestAsync(int limit, string? startAfterCursor = null)
    {
        _logger.LogInfo($"InvoiceRepo.LatestAsync limit={limit}, cursor={startAfterCursor ?? "(none)"}");
        try
        {
            return await _inner.LatestAsync(limit, startAfterCursor);
        }
        catch (Exception ex)
        {
            _logger.LogError("InvoiceRepo.LatestAsync failed", ex);
            throw;
        }
    }
}
