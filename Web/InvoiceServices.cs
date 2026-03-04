using Accounting;
using Invoices;
using Utilities;

namespace Web;

public interface IInvoiceOperations
{
    Task<QueryResult<Invoice>> ListInvoicesAsync(int limit, string? startAfterCursor);
    Task<Invoice> GetInvoiceAsync(string number);
    Task<InvoiceOperationResult> IssueInvoiceAsync(string clientNickname, int amountCents, DateTime? date, IInvoiceExporter exporter);
    Task<InvoiceOperationResult> CorrectInvoiceAsync(string invoiceNumber, int? amountCents, DateTime? date, IInvoiceExporter exporter);
    Task<InvoiceOperationResult> ReExportInvoiceAsync(string invoiceNumber, IInvoiceExporter exporter);
    Task<ExportResult> PreviewInvoiceAsync(string clientNickname, int amountCents, DateTime? date, IInvoiceExporter exporter, string? invoiceNumber = null);
    Task<Invoice> ImportLegacyInvoiceAsync(LegacyInvoiceData data, byte[]? sourcePdfBytes = null);
    Task<(Stream Stream, string DownloadFileName)> GetInvoicePdfStreamAsync(string number);
}

public sealed class InvoiceManagementAdapter : IInvoiceOperations
{
    private readonly InvoiceManagement _inner;

    public InvoiceManagementAdapter(InvoiceManagement inner)
    {
        _inner = inner;
    }

    public Task<QueryResult<Invoice>> ListInvoicesAsync(int limit, string? startAfterCursor) =>
        _inner.ListInvoicesAsync(limit, startAfterCursor);

    public Task<Invoice> GetInvoiceAsync(string number) =>
        _inner.GetInvoiceAsync(number);

    public Task<InvoiceOperationResult> IssueInvoiceAsync(string clientNickname, int amountCents, DateTime? date, IInvoiceExporter exporter) =>
        _inner.IssueInvoiceAsync(clientNickname, amountCents, date, exporter);

    public Task<InvoiceOperationResult> CorrectInvoiceAsync(string invoiceNumber, int? amountCents, DateTime? date, IInvoiceExporter exporter) =>
        _inner.CorrectInvoiceAsync(invoiceNumber, amountCents, date, exporter);

    public Task<InvoiceOperationResult> ReExportInvoiceAsync(string invoiceNumber, IInvoiceExporter exporter) =>
        _inner.ReExportInvoiceAsync(invoiceNumber, exporter);

    public Task<ExportResult> PreviewInvoiceAsync(string clientNickname, int amountCents, DateTime? date, IInvoiceExporter exporter, string? invoiceNumber = null) =>
        _inner.PreviewInvoiceAsync(clientNickname, amountCents, date, exporter, invoiceNumber);

    public Task<Invoice> ImportLegacyInvoiceAsync(LegacyInvoiceData data, byte[]? sourcePdfBytes = null) =>
        _inner.ImportLegacyInvoiceAsync(data, sourcePdfBytes);

    public Task<(Stream Stream, string DownloadFileName)> GetInvoicePdfStreamAsync(string number) =>
        _inner.GetInvoicePdfStreamAsync(number);
}

public interface IPdfInvoiceExporter
{
    IInvoiceExporter Exporter { get; }
}

public interface IImageInvoiceExporter
{
    IInvoiceExporter Exporter { get; }
}

public sealed class PdfInvoiceExporterAdapter : IPdfInvoiceExporter
{
    public PdfInvoiceExporterAdapter(IInvoiceExporter exporter)
    {
        Exporter = exporter;
    }

    public IInvoiceExporter Exporter { get; }
}

public sealed class ImageInvoiceExporterAdapter : IImageInvoiceExporter
{
    public ImageInvoiceExporterAdapter(IInvoiceExporter exporter)
    {
        Exporter = exporter;
    }

    public IInvoiceExporter Exporter { get; }
}
