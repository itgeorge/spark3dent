using Microsoft.AspNetCore.Http;

namespace Web;

public record ImportAnalyzeRequest(
    IReadOnlyList<IFormFile> Files,
    ImportAnalyzeOptions Options,
    string OpenAiKey);

public interface IInvoiceImporter
{
    Task<ImportAnalyzeResponse> AnalyzeAsync(ImportAnalyzeRequest request, CancellationToken cancellationToken = default);
    Task<ImportCommitResponse> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Temporary default importer used until Phase 4 implementation lands.
/// </summary>
public sealed class NoopInvoiceImporter : IInvoiceImporter
{
    public Task<ImportAnalyzeResponse> AnalyzeAsync(ImportAnalyzeRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ImportAnalyzeResponse(Array.Empty<ImportAnalyzeFileResult>(), Array.Empty<string>()));

    public Task<ImportCommitResponse> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ImportCommitResponse(0, 0, 0, Array.Empty<ImportCommitItemStatus>()));
}
