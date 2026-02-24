using System.Net;
using System.Net.Http;
using System.Text.Json;
using NUnit.Framework;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Web.Tests;

public class ApiTestFixture : WebApplicationFactory<Program>
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _blobPath;
    private readonly string _startInvoiceNumber;

    public ApiTestFixture(string startInvoiceNumber = "1")
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WebTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _blobPath = Path.Combine(_tempDir, "blob");
        _startInvoiceNumber = startInvoiceNumber;
    }

    public HttpClient Client => CreateClient();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Port"] = "0",
                ["Desktop:DatabasePath"] = _dbPath,
                ["Desktop:BlobStoragePath"] = _blobPath,
                ["Desktop:LogDirectory"] = _tempDir,
                ["App:StartInvoiceNumber"] = _startInvoiceNumber
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Port"] = "0",
                ["Desktop:DatabasePath"] = _dbPath,
                ["Desktop:BlobStoragePath"] = _blobPath,
                ["Desktop:LogDirectory"] = _tempDir,
                ["App:StartInvoiceNumber"] = _startInvoiceNumber
            });
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
