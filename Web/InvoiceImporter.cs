using System.Diagnostics;
using Accounting;
using Invoices;
using Microsoft.AspNetCore.Http;
using Storage;
using Utilities;

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

public interface ILegacyInvoiceParser
{
    Task<LegacyInvoiceData?> TryParseAsync(byte[] pdfBytes, string openAiKey, CancellationToken cancellationToken = default);
}

public sealed class GptLegacyInvoiceParser : ILegacyInvoiceParser
{
    public Task<LegacyInvoiceData?> TryParseAsync(byte[] pdfBytes, string openAiKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(LegacyPdfParser.TryParse(pdfBytes));
}

public sealed class InvoiceImporter : IInvoiceImporter
{
    private const string AnalyzeArtifactPrefix = "analyze";
    private readonly IClientRepo _clientRepo;
    private readonly IInvoiceOperations _invoiceOps;
    private readonly ILegacyInvoiceParser _parser;
    private readonly IBlobStorage _blobStorage;
    private readonly string _tempBucket;
    private readonly Utilities.ILogger? _logger;

    public InvoiceImporter(
        IClientRepo clientRepo,
        IInvoiceOperations invoiceOps,
        ILegacyInvoiceParser parser,
        IBlobStorage blobStorage,
        string tempBucket,
        Utilities.ILogger? logger = null)
    {
        _clientRepo = clientRepo;
        _invoiceOps = invoiceOps;
        _parser = parser;
        _blobStorage = blobStorage;
        _tempBucket = tempBucket;
        _logger = logger;
    }

    public async Task<ImportAnalyzeResponse> AnalyzeAsync(ImportAnalyzeRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var files = request.Files;
        if (request.Options.Limit is > 0)
            files = files.Take(request.Options.Limit.Value).ToArray();

        var results = new List<ImportAnalyzeFileResult>();
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var pdfBytes = await ReadAllBytesAsync(file, cancellationToken);

            var parsed = await _parser.TryParseAsync(pdfBytes, request.OpenAiKey, cancellationToken);
            if (parsed == null)
            {
                results.Add(ImportAnalyzeFileResult.ForError(file.FileName, "Parse failed"));
                continue;
            }

            var token = Guid.NewGuid().ToString("N");
            await UploadAnalyzeArtifactAsync(token, pdfBytes, cancellationToken);
            var existing = await _clientRepo.FindByCompanyIdentifierAsync(parsed.Recipient.CompanyIdentifier);
            if (existing == null)
                unresolved.Add(parsed.Recipient.CompanyIdentifier);

            results.Add(new ImportAnalyzeFileResult(
                file.FileName,
                token,
                parsed.Number,
                parsed.Date.ToString("yyyy-MM-dd"),
                parsed.TotalCents,
                parsed.Recipient.CompanyIdentifier,
                parsed.Recipient.Name,
                parsed.Recipient.RepresentativeName,
                parsed.Recipient.Address,
                parsed.Recipient.City,
                parsed.Recipient.PostalCode,
                parsed.Recipient.Country,
                null));
        }

        sw.Stop();
        var okCount = results.Count(r => r.Error == null);
        var errorCount = results.Count(r => r.Error != null);
        _logger?.LogInfo($"Import analyze completed: {results.Count} files, {okCount} parsed, {errorCount} errors, {unresolved.Count} unresolved companies, {sw.ElapsedMilliseconds}ms");
        return new ImportAnalyzeResponse(results.ToArray(), unresolved.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<ImportCommitResponse> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var statuses = new List<ImportCommitItemStatus>();
        var imported = 0;
        var skipped = 0;
        var failed = 0;
        var items = request.Items ?? Array.Empty<ImportCommitItem>();

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.CompanyIdentifier) ||
                string.IsNullOrWhiteSpace(item.InvoiceNumber) ||
                !item.TotalCents.HasValue ||
                !DateTime.TryParse(item.Date, out var date))
            {
                failed++;
                statuses.Add(new ImportCommitItemStatus(item.FileName, "failed", "Invalid item payload"));
                continue;
            }

            try
            {
                var existing = await _clientRepo.FindByCompanyIdentifierAsync(item.CompanyIdentifier);
                var nickname = existing?.Nickname ?? ResolveNickname(item, request.NicknameMap);
                if (string.IsNullOrWhiteSpace(nickname))
                {
                    failed++;
                    statuses.Add(new ImportCommitItemStatus(item.FileName, "failed", $"Missing nickname mapping for {item.CompanyIdentifier}"));
                    continue;
                }

                if (request.DryRun)
                {
                    imported++;
                    statuses.Add(new ImportCommitItemStatus(item.FileName, "imported", "dry-run"));
                    continue;
                }

                if (existing == null)
                {
                    var recipient = BuildRecipient(item);
                    await _clientRepo.AddAsync(new Client(nickname, recipient));
                }

                var data = new LegacyInvoiceData(item.InvoiceNumber, date, item.TotalCents.Value, Currency.Eur, BuildRecipient(item));
                var sourcePdfBytes = await ResolveAndConsumePdfBytesAsync(item.TempFileToken, cancellationToken);
                await _invoiceOps.ImportLegacyInvoiceAsync(data, sourcePdfBytes);
                imported++;
                statuses.Add(new ImportCommitItemStatus(item.FileName, "imported", null));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
            {
                skipped++;
                statuses.Add(new ImportCommitItemStatus(item.FileName, "skipped", "already exists"));
            }
            catch (Exception ex)
            {
                failed++;
                statuses.Add(new ImportCommitItemStatus(item.FileName, "failed", ex.Message));
            }
        }

        sw.Stop();
        _logger?.LogInfo($"Import commit completed: {imported} imported, {skipped} skipped, {failed} failed, {sw.ElapsedMilliseconds}ms");
        return new ImportCommitResponse(imported, skipped, failed, statuses.ToArray());
    }

    private async Task UploadAnalyzeArtifactAsync(string token, byte[] pdfBytes, CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream(pdfBytes, writable: false);
        var objectKey = BuildAnalyzeObjectKey(token);
        await _blobStorage.UploadAsync(_tempBucket, objectKey, ms, "application/pdf");
    }

    private async Task<byte[]?> ResolveAndConsumePdfBytesAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var objectKey = BuildAnalyzeObjectKey(token);
        try
        {
            await using var stream = await _blobStorage.OpenReadAsync(_tempBucket, objectKey);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            return ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        finally
        {
            try { await _blobStorage.DeleteAsync(_tempBucket, objectKey); } catch { /* best effort */ }
        }
    }

    private static BillingAddress BuildRecipient(ImportCommitItem item) =>
        new(
            item.Name ?? item.CompanyIdentifier ?? "",
            item.RepresentativeName ?? "",
            item.CompanyIdentifier ?? "",
            null,
            item.Address ?? "",
            item.City ?? "",
            item.PostalCode ?? "",
            item.Country ?? "");

    private static string? ResolveNickname(ImportCommitItem item, Dictionary<string, string>? nicknameMap)
    {
        if (item.CompanyIdentifier != null &&
            nicknameMap != null &&
            nicknameMap.TryGetValue(item.CompanyIdentifier, out var mapped) &&
            !string.IsNullOrWhiteSpace(mapped))
            return mapped.Trim();

        var molSlug = ToSlug(item.RepresentativeName);
        if (!string.IsNullOrWhiteSpace(molSlug))
            return molSlug;

        var nameSlug = ToSlug(item.Name);
        if (!string.IsNullOrWhiteSpace(nameSlug))
            return nameSlug;

        return item.CompanyIdentifier;
    }

    private static string ToSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '-' || c == '.')
                sb.Append('-');
        }
        var slug = string.Join("-", sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 40 ? slug[..40] : slug;
    }

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    private static string BuildAnalyzeObjectKey(string token) => $"{AnalyzeArtifactPrefix}/{token}";
}
