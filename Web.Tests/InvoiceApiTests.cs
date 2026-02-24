using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class InvoiceApiTests
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

    private async Task<string> CreateClientAsync()
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
        var response = await _client.PostAsync("/api/clients", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return "acme";
    }

    [Test]
    public async Task GetInvoices_WhenEmptyDb_Returns200WithEmptyItems()
    {
        var response = await _client.GetAsync("/api/invoices?limit=10");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task PostIssue_WithValidBody_Returns200WithInvoiceNumber()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme", amountCents = 15000, date = "2026-02-20" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/invoices/issue", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var inv = doc.RootElement.GetProperty("invoice");
        Assert.That(inv.GetProperty("number").GetString(), Is.Not.Null.And.Not.Empty);
        Assert.That(inv.GetProperty("totalCents").GetInt32(), Is.EqualTo(15000));
    }

    [Test]
    public async Task PostIssue_WithNonExistingClient_Returns404()
    {
        var body = new { clientNickname = "nonexistent", amountCents = 10000 };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/issue", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PostIssue_WithNegativeAmount_Returns400()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme", amountCents = -100 };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/issue", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PostIssue_WithMissingClientNickname_Returns400()
    {
        var body = new { amountCents = 10000 };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/issue", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PostIssue_WithMissingAmountCents_Returns400()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/issue", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostIssue_WithInvalidDateFormat_Returns400()
    {
        await CreateClientAsync();
        var body = new { clientNickname = "acme", amountCents = 10000, date = "invalid" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/issue", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetInvoice_WhenExists_Returns200WithFullDetails()
    {
        await CreateClientAsync();
        var issueBody = new { clientNickname = "acme", amountCents = 12345, date = "2026-02-20" };
        var issueContent = new StringContent(JsonSerializer.Serialize(issueBody), Encoding.UTF8, "application/json");
        var issueResponse = await _client.PostAsync("/api/invoices/issue", issueContent);
        var issueJson = await issueResponse.Content.ReadAsStringAsync();
        var number = JsonDocument.Parse(issueJson).RootElement.GetProperty("invoice").GetProperty("number").GetString()!;

        var response = await _client.GetAsync($"/api/invoices/{number}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("number").GetString(), Is.EqualTo(number));
        Assert.That(doc.RootElement.GetProperty("totalCents").GetInt32(), Is.EqualTo(12345));
    }

    [Test]
    public async Task GetInvoice_WhenNotExists_Returns404()
    {
        var response = await _client.GetAsync("/api/invoices/99999");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PostCorrect_WithValidBody_Returns200()
    {
        await CreateClientAsync();
        var issueBody = new { clientNickname = "acme", amountCents = 10000, date = "2026-02-20" };
        var issueContent = new StringContent(JsonSerializer.Serialize(issueBody), Encoding.UTF8, "application/json");
        var issueResponse = await _client.PostAsync("/api/invoices/issue", issueContent);
        var issueJson = await issueResponse.Content.ReadAsStringAsync();
        var number = JsonDocument.Parse(issueJson).RootElement.GetProperty("invoice").GetProperty("number").GetString()!;

        var correctBody = new { invoiceNumber = number, amountCents = 20000, date = "2026-02-21" };
        var correctContent = new StringContent(JsonSerializer.Serialize(correctBody), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/correct", correctContent);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("invoice").GetProperty("totalCents").GetInt32(), Is.EqualTo(20000));
    }

    [Test]
    public async Task PostCorrect_ForNonExistingInvoice_Returns404()
    {
        var body = new { invoiceNumber = "99999", amountCents = 10000 };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/invoices/correct", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetInvoices_AfterIssuingMultiple_ReturnsNewestFirst()
    {
        await CreateClientAsync();
        for (var i = 0; i < 3; i++)
        {
            var body = new { clientNickname = "acme", amountCents = (i + 1) * 1000, date = $"2026-02-2{i + 1}" };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            await _client.PostAsync("/api/invoices/issue", content);
        }

        var response = await _client.GetAsync("/api/invoices?limit=10");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(3));
        var numbers = items.EnumerateArray().Select(x => x.GetProperty("number").GetString()).ToList();
        Assert.That(long.Parse(numbers[0]!), Is.GreaterThan(long.Parse(numbers[2]!)));
    }
}
