using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AppSetup;
using Configuration;
using Database;
using Invoices;

namespace Web.Tests;

[TestFixture]
public class InvoiceExportApiTests
{
    private ApiTestFixture _fixture = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new ApiTestFixture();
        _client = _fixture.Client;
    }

    [TearDown]
    public void TearDown() => _fixture.Dispose();

    private async Task<string> CreateLegacyInvoiceInDbAsync()
    {
        var config = _fixture.Server.Services.GetRequiredService<IConfiguration>();
        var configObj = config.Get<Config>() ?? new Config();
        configObj.Desktop ??= new DesktopConfig();
        configObj.Desktop.DatabasePath = config["Desktop:DatabasePath"] ?? configObj.Desktop.DatabasePath;
        if (string.IsNullOrEmpty(configObj.Desktop.DatabasePath))
            throw new InvalidOperationException("Desktop:DatabasePath not configured");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={configObj.Desktop.DatabasePath}")
            .Options;

        AppDbContext ContextFactory() => new AppDbContext(options);
        var repo = new SqliteInvoiceRepo(ContextFactory, configObj);

        var seller = configObj.App.SellerAddress != null
            ? AppBootstrap.ConfigToBillingAddress(configObj.App.SellerAddress)
            : new BillingAddress("Test", "Test", "123", null, "Addr", "City", "1000", "BG");
        var bank = configObj.App.SellerBankTransferInfo != null
            ? AppBootstrap.ConfigToBankTransferInfo(configObj.App.SellerBankTransferInfo)
            : new BankTransferInfo("BG00BANK", "Bank", "BIC");
        var buyer = new BillingAddress("ACME EOOD", "John Doe", "BG123456789", null, "1 Main St", "Sofia", "1000", "Bulgaria");
        var content = new Invoice.InvoiceContent(
            DateTime.UtcNow.Date,
            seller,
            buyer,
            new[] { new Invoice.LineItem("Зъботехнически услуги", new Amount(15000, Currency.Eur)) },
            bank);

        var number = "0000000999";
        var invoice = await repo.ImportAsync(content, number);
        return invoice.Number;
    }

    private async Task CreateClientAsync()
    {
        await CreateClientInFixtureAsync(_client);
    }

    private static async Task CreateClientInFixtureAsync(HttpClient client)
    {
        var body = new
        {
            nickname = "acme",
            name = "ACME EOOD",
            representativeName = "John Doe",
            companyIdentifier = "BG123456789",
            vatIdentifier = (string?)null,
            address = "1 Main St",
            city = "Sofia",
            postalCode = "1000",
            country = "Bulgaria"
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        await client.PostAsync("/api/clients", content);
    }

    [Test]
    public async Task PostPreview_WithFormatHtml_Returns200WithHtmlContent()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme", amountCents = 15000, date = "2026-02-20", format = "html" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(html, Is.Not.Null.And.Not.Empty);
        Assert.That(html, Does.Contain("ACME EOOD"));
        Assert.That(html.Length, Is.GreaterThan(100));
    }

    [Test]
    public async Task PostPreview_WhenNoInvoicesExist_ThenUsesStartInvoiceNumberFromConfig()
    {
        using var fixtureWithStart1000 = new ApiTestFixture(startInvoiceNumber: "1000");
        var client = fixtureWithStart1000.CreateClient();
        await CreateClientInFixtureAsync(client);
        var body = new { clientNickname = "acme", amountCents = 15000, date = "2026-02-20", format = "html" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(html, Does.Contain("1000"), "When no invoices exist, preview should use StartInvoiceNumber from appsettings.json");
    }

    [Test]
    public async Task PostPreview_WithInvoiceNumber_ReturnsHtmlWithThatNumber()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme", amountCents = 25000, date = "2026-02-20", format = "html", invoiceNumber = "0000000042" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(html, Does.Contain("0000000042"));
    }

    [Test]
    public async Task PostPreview_WithFormatOmitted_DefaultsToHtml()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme", amountCents = 10000, date = "2026-02-20" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
    }

    [Test]
    public async Task PostPreview_WithFormatHtml_MissingClientNickname_Returns400()
    {
        var body = new { amountCents = 10000, format = "html" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PostPreview_WithFormatHtml_NonExistingClient_Returns404()
    {
        var body = new { clientNickname = "nonexistent", amountCents = 10000, format = "html" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PostPreview_WithInvalidFormat_Returns400()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme", amountCents = 10000, format = "pdf" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PostPreview_WithFormatPng_Returns503Or200()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme", amountCents = 10000, format = "png" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable));
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.ServiceUnavailable);
        else
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
    }

    [Test]
    public async Task PostPreview_WithLegacyInvoiceNumber_Returns400()
    {
        await CreateClientAsync();
        var legacyNumber = await CreateLegacyInvoiceInDbAsync();
        var body = new { clientNickname = "acme", amountCents = 15000, date = "2026-02-20", format = "html", invoiceNumber = legacyNumber };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/invoices/preview", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("error", out var err), Is.True);
        Assert.That(err.GetString(), Does.Contain("legacy").Or.Contain("Legacy"));
    }

    [Test]
    public async Task GetPdf_ForNonExistingInvoice_Returns404()
    {
        var response = await _client.GetAsync("/api/invoices/99999/pdf");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetPdf_WhenInvoiceExists_Returns200Or404()
    {
        await CreateClientAsync();
        var issueBody = new { clientNickname = "acme", amountCents = 10000, date = "2026-02-20" };
        var issueContent = new StringContent(JsonSerializer.Serialize(issueBody), Encoding.UTF8, "application/json");
        var issueResponse = await _client.PostAsync("/api/invoices/issue", issueContent);
        var number = JsonDocument.Parse(await issueResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("invoice").GetProperty("number").GetString()!;

        var response = await _client.GetAsync($"/api/invoices/{number}/pdf");
        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.NotFound));
        if (response.StatusCode == HttpStatusCode.OK)
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/pdf"));
        else
            await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }
}
