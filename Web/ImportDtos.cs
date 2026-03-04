namespace Web;

/// <summary>Request options for analyze (from form fields).</summary>
public record ImportAnalyzeOptions(bool NicknameFromMol = false, int? Limit = null);

/// <summary>Per-file result from analyze.</summary>
public record ImportAnalyzeFileResult(
    string FileName,
    string? TempFileToken,
    string? InvoiceNumber,
    string? Date,
    int? TotalCents,
    string? CompanyIdentifier,
    string? Name,
    string? RepresentativeName,
    string? Address,
    string? City,
    string? PostalCode,
    string? Country,
    string? Error)
{
    /// <summary>Creates an error result for a file that failed to parse.</summary>
    public static ImportAnalyzeFileResult ForError(string fileName, string message) =>
        new(fileName, null, null, null, null, null, null, null, null, null, null, null, message);
}

/// <summary>Response from POST /api/invoices/import/analyze.</summary>
public record ImportAnalyzeResponse(
    ImportAnalyzeFileResult[] Files,
    string[] UnresolvedCompanies);

/// <summary>Request for POST /api/invoices/import/commit.</summary>
public record ImportCommitRequest(
    ImportCommitItem[]? Items,
    Dictionary<string, string>? NicknameMap,
    bool DryRun = false);

/// <summary>Single item from analyze to import.</summary>
public record ImportCommitItem(
    string FileName,
    string? TempFileToken,
    string? InvoiceNumber,
    string? Date,
    int? TotalCents,
    string? CompanyIdentifier,
    string? Name,
    string? RepresentativeName,
    string? Address,
    string? City,
    string? PostalCode,
    string? Country);

/// <summary>Per-item status from commit.</summary>
public record ImportCommitItemStatus(
    string FileName,
    string Status, // "imported" | "skipped" | "failed"
    string? Message);

/// <summary>Response from POST /api/invoices/import/commit.</summary>
public record ImportCommitResponse(
    int Imported,
    int Skipped,
    int Failed,
    ImportCommitItemStatus[] ItemStatuses);
