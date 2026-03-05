using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Utilities;
using Web;

namespace Web.Tests;

public class ApiTestFixture : WebApplicationFactory<Program>
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _blobPath;
    private readonly string _startInvoiceNumber;
    private readonly string _runtimeHostingMode;
    private readonly string? _runtimePort;
    private readonly string? _openAiKey;
    private readonly IInvoiceImporter? _invoiceImporterOverride;
    private readonly Utilities.ILogger? _loggerOverride;

    public ApiTestFixture(
        string startInvoiceNumber = "1",
        string runtimeHostingMode = "Desktop",
        string? runtimePort = "0",
        string? openAiKey = null,
        IInvoiceImporter? invoiceImporterOverride = null,
        Utilities.ILogger? loggerOverride = null)
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WebTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _blobPath = Path.Combine(_tempDir, "blob");
        _startInvoiceNumber = startInvoiceNumber;
        _runtimeHostingMode = runtimeHostingMode;
        _runtimePort = runtimePort;
        _openAiKey = openAiKey;
        _invoiceImporterOverride = invoiceImporterOverride;
        _loggerOverride = loggerOverride;
    }

    public HttpClient Client => CreateClient();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var inMemory = new Dictionary<string, string?>
            {
                ["Runtime:HostingMode"] = _runtimeHostingMode,
                ["SingleBox:DatabasePath"] = _dbPath,
                ["SingleBox:BlobStoragePath"] = _blobPath,
                ["SingleBox:LogDirectory"] = _tempDir,
                ["App:StartInvoiceNumber"] = _startInvoiceNumber
            };
            if (_runtimePort != null)
                inMemory["Runtime:Port"] = _runtimePort;
            if (_openAiKey != null)
                inMemory["App:OpenAiKey"] = _openAiKey;
            config.AddInMemoryCollection(inMemory);
        });

        builder.ConfigureTestServices(services =>
        {
            if (_loggerOverride != null)
            {
                services.RemoveAll<Utilities.ILogger>();
                services.AddSingleton(_loggerOverride);
            }
            if (_invoiceImporterOverride != null)
            {
                services.RemoveAll<IInvoiceImporter>();
                services.AddSingleton(_invoiceImporterOverride);
            }
            // Use LegacyOnlyInvoiceParser so API tests don't need OpenAI key
            services.RemoveAll<ILegacyInvoiceParser>();
            services.AddSingleton<ILegacyInvoiceParser, LegacyOnlyInvoiceParser>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureHostConfiguration(config =>
        {
            var inMemory = new Dictionary<string, string?>
            {
                ["Runtime:HostingMode"] = _runtimeHostingMode,
                ["SingleBox:DatabasePath"] = _dbPath,
                ["SingleBox:BlobStoragePath"] = _blobPath,
                ["SingleBox:LogDirectory"] = _tempDir,
                ["App:StartInvoiceNumber"] = _startInvoiceNumber
            };
            if (_runtimePort != null)
                inMemory["Runtime:Port"] = _runtimePort;
            if (_openAiKey != null)
                inMemory["App:OpenAiKey"] = _openAiKey;
            config.AddInMemoryCollection(inMemory);
        });
        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* ignore */ }
        }
        base.Dispose(disposing);
    }

    public static async Task AssertJsonErrorAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("error", out var err), Is.True);
        Assert.That(err.GetString(), Is.Not.Null.And.Not.Empty);
    }
}
