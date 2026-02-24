using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

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

    private async Task CreateClientAsync()
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
        await _client.PostAsync("/api/clients", content);
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
