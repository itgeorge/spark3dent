using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Utilities.Tests;

namespace Web.Tests;

[TestFixture]
public class InvoiceImportApiTests
{
    private sealed class FakeInvoiceImporter : IInvoiceImporter
    {
        private readonly ImportAnalyzeResponse _analyzeResponse;
        private readonly ImportCommitResponse _commitResponse;

        public FakeInvoiceImporter(ImportAnalyzeResponse analyzeResponse, ImportCommitResponse commitResponse)
        {
            _analyzeResponse = analyzeResponse;
            _commitResponse = commitResponse;
        }

        public Task<ImportAnalyzeResponse> AnalyzeAsync(ImportAnalyzeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_analyzeResponse);

        public Task<ImportCommitResponse> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_commitResponse);
    }

    private static string MinimalPdfPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "minimal.pdf");
    [Test]
    public async Task PostImportAnalyze_WhenOpenAiKeyNotConfigured_Returns500AndLogsOpenAiError()
    {
        var capturingLogger = new CapturingLogger();
        using var fixture = new ApiTestFixture(openAiKey: null, loggerOverride: capturingLogger);
        var client = fixture.Client;

        var content = new MultipartFormDataContent();
        var response = await client.PostAsync("/api/invoices/import/analyze", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.InternalServerError);
        Assert.That(capturingLogger.ErrorEntries, Has.Count.GreaterThanOrEqualTo(1));
        var loggedMessage = capturingLogger.ErrorEntries[0].Message;
        Assert.That(loggedMessage, Does.Contain("OpenAI").Or.Contain("OpenAi").Or.Contain("API key"));
    }

    [Test]
    public async Task PostImportCommit_WhenOpenAiKeyNotConfigured_Returns500AndLogsOpenAiError()
    {
        var capturingLogger = new CapturingLogger();
        using var fixture = new ApiTestFixture(openAiKey: null, loggerOverride: capturingLogger);
        var client = fixture.Client;

        var body = new { items = Array.Empty<object>(), nicknameMap = new Dictionary<string, string>() };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/invoices/import/commit", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.InternalServerError);
        Assert.That(capturingLogger.ErrorEntries, Has.Count.GreaterThanOrEqualTo(1));
        var loggedMessage = capturingLogger.ErrorEntries[0].Message;
        Assert.That(loggedMessage, Does.Contain("OpenAI").Or.Contain("OpenAi").Or.Contain("API key"));
    }

    [Test]
    public async Task PostImportAnalyze_WhenOpenAiKeyConfigured_DoesNotReturn500ForMissingKey()
    {
        using var fixture = new ApiTestFixture(openAiKey: "sk-test-dummy-key");
        var client = fixture.Client;

        var content = new MultipartFormDataContent();
        var response = await client.PostAsync("/api/invoices/import/analyze", content);

        // Key is present, so we should NOT get 500 (missing key). We may get 400 for no files.
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public async Task PostImportCommit_WhenOpenAiKeyConfigured_DoesNotReturn500ForMissingKey()
    {
        using var fixture = new ApiTestFixture(openAiKey: "sk-dummy");
        var client = fixture.Client;

        var body = new { items = Array.Empty<object>(), nicknameMap = new Dictionary<string, string>() };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/invoices/import/commit", content);

        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public async Task PostImportAnalyze_WithInvalidMultipartForm_Returns422WithDevFriendlyMessage()
    {
        using var fixture = new ApiTestFixture(openAiKey: "sk-dummy");
        var content = new MultipartFormDataContent();
        // Empty MultipartFormDataContent produces invalid multipart body (no valid Content-Disposition).
        var response = await fixture.Client.PostAsync("/api/invoices/import/analyze", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadAsStringAsync();
        var error = JsonDocument.Parse(json).RootElement.GetProperty("error").GetString();
        Assert.That(error, Does.Contain("form").Or.Contain("multipart").Or.Contain("Content-Disposition"),
            "Error message should be dev-friendly and hint at the form/multipart issue");
    }

    [Test]
    public async Task PostImportAnalyze_WithNonPdfFile_Returns400WithError()
    {
        using var fixture = new ApiTestFixture(openAiKey: "sk-dummy");
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("not a pdf"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "files", "document.txt");

        var response = await fixture.Client.PostAsync("/api/invoices/import/analyze", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        var error = JsonDocument.Parse(json).RootElement.GetProperty("error").GetString();
        Assert.That(error, Does.Contain("pdf").Or.Contain("PDF"));
    }

    [Test]
    public async Task PostImportAnalyze_WithValidPdf_Returns200WithExpectedShape()
    {
        var pdfPath = MinimalPdfPath;
        if (!File.Exists(pdfPath))
        {
            Assert.Ignore($"Minimal PDF not found at {pdfPath}");
            return;
        }

        using var fixture = new ApiTestFixture(openAiKey: "sk-dummy");
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(pdfPath));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "files", "invoice.pdf");

        var response = await fixture.Client.PostAsync("/api/invoices/import/analyze", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("files", out var files), Is.True);
        Assert.That(files.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(doc.RootElement.TryGetProperty("unresolvedCompanies", out var unresolved), Is.True);
        Assert.That(unresolved.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task PostImportAnalyze_UsesDiInjectedImporterResponse()
    {
        var pdfPath = MinimalPdfPath;
        if (!File.Exists(pdfPath))
        {
            Assert.Ignore($"Minimal PDF not found at {pdfPath}");
            return;
        }

        var fakeAnalyze = new ImportAnalyzeResponse(
            [new ImportAnalyzeFileResult("invoice.pdf", "tok", "42", "2026-01-01", 12345, "Eur", "BG123", "ACME", "John", "addr", "Sofia", "1000", "Bulgaria", null)],
            ["BG999"],
            new Dictionary<string, string> { ["BG999"] = "john" });
        var fakeCommit = new ImportCommitResponse(0, 0, 0, []);
        using var fixture = new ApiTestFixture(
            openAiKey: "sk-dummy",
            invoiceImporterOverride: new FakeInvoiceImporter(fakeAnalyze, fakeCommit));

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(pdfPath));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "files", "invoice.pdf");

        var response = await fixture.Client.PostAsync("/api/invoices/import/analyze", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.GetProperty("files")[0].GetProperty("invoiceNumber").GetString(), Is.EqualTo("42"));
        Assert.That(doc.RootElement.GetProperty("unresolvedCompanies")[0].GetString(), Is.EqualTo("BG999"));
    }

    [Test]
    public async Task PostImportCommit_WithInvalidJson_Returns400()
    {
        using var fixture = new ApiTestFixture(openAiKey: "sk-dummy");
        var content = new StringContent("{ invalid json }", Encoding.UTF8, "application/json");
        var response = await fixture.Client.PostAsync("/api/invoices/import/commit", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PostImportCommit_WithValidPayload_Returns200WithExpectedShape()
    {
        using var fixture = new ApiTestFixture(openAiKey: "sk-dummy");
        var body = new { items = Array.Empty<object>(), nicknameMap = new Dictionary<string, string>(), dryRun = false };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await fixture.Client.PostAsync("/api/invoices/import/commit", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("imported", out var imported), Is.True);
        Assert.That(doc.RootElement.TryGetProperty("skipped", out var skipped), Is.True);
        Assert.That(doc.RootElement.TryGetProperty("failed", out var failed), Is.True);
        Assert.That(doc.RootElement.TryGetProperty("itemStatuses", out var statuses), Is.True);
        Assert.That(statuses.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task PostImportCommit_UsesDiInjectedImporterResponse()
    {
        var fakeAnalyze = new ImportAnalyzeResponse([], []);
        var fakeCommit = new ImportCommitResponse(
            1,
            2,
            3,
            [new ImportCommitItemStatus("invoice.pdf", "failed", "parse failed")]);
        using var fixture = new ApiTestFixture(
            openAiKey: "sk-dummy",
            invoiceImporterOverride: new FakeInvoiceImporter(fakeAnalyze, fakeCommit));

        var body = new { items = Array.Empty<object>(), nicknameMap = new Dictionary<string, string>(), dryRun = false };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await fixture.Client.PostAsync("/api/invoices/import/commit", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.GetProperty("imported").GetInt32(), Is.EqualTo(1));
        Assert.That(doc.RootElement.GetProperty("skipped").GetInt32(), Is.EqualTo(2));
        Assert.That(doc.RootElement.GetProperty("failed").GetInt32(), Is.EqualTo(3));
        Assert.That(doc.RootElement.GetProperty("itemStatuses")[0].GetProperty("status").GetString(), Is.EqualTo("failed"));
    }

    [Test]
    public async Task PostImportAnalyze_WithTooManyFiles_Returns400()
    {
        using var fixture = new ApiTestFixture(openAiKey: "sk-dummy");
        var pdfPath = MinimalPdfPath;
        if (!File.Exists(pdfPath))
        {
            Assert.Ignore($"Minimal PDF not found at {pdfPath}");
            return;
        }

        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        var content = new MultipartFormDataContent();
        for (var i = 0; i < 501; i++)
        {
            var fileContent = new ByteArrayContent(pdfBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(fileContent, "files", $"invoice{i}.pdf");
        }

        var response = await fixture.Client.PostAsync("/api/invoices/import/analyze", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PostImportAnalyze_WithFileTooLarge_Returns400()
    {
        using var fixture = new ApiTestFixture(openAiKey: "sk-dummy");
        var content = new MultipartFormDataContent();
        var oversized = new byte[1024 * 1024 + 1]; // 1MB + 1 byte
        new Random(42).NextBytes(oversized);
        oversized[0] = (byte)'%';
        oversized[1] = (byte)'P';
        oversized[2] = (byte)'D';
        oversized[3] = (byte)'F';
        var fileContent = new ByteArrayContent(oversized);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "files", "large.pdf");

        var response = await fixture.Client.PostAsync("/api/invoices/import/analyze", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }
}
