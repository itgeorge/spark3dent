using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class ClientApiTests
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

    [Test]
    public async Task GetClients_WhenEmptyDb_Returns200WithEmptyItems()
    {
        var response = await _client.GetAsync("/api/clients?limit=10");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task PostClients_WithValidBody_Returns201AndCreatedClient()
    {
        var body = new
        {
            nickname = "acme",
            name = "ACME EOOD",
            representativeName = "John Doe",
            companyIdentifier = "BG123456789",
            vatIdentifier = "BG123456789",
            address = "1 Main St",
            city = "Sofia",
            postalCode = "1000",
            country = "Bulgaria"
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/clients", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("nickname").GetString(), Is.EqualTo("acme"));
        Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("ACME EOOD"));
    }

    [Test]
    public async Task PostClients_WithDuplicateNickname_Returns409()
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

        var response = await _client.PostAsync("/api/clients", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.Conflict);
    }

    [Test]
    public async Task PostClients_WithMissingName_Returns400()
    {
        var body = new
        {
            nickname = "acme",
            name = "",
            representativeName = "John Doe",
            companyIdentifier = "BG123456789",
            address = "1 Main St",
            city = "Sofia",
            postalCode = "1000",
            country = "Bulgaria"
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/clients", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PostClients_WithMissingNickname_Returns400()
    {
        var body = new
        {
            nickname = "",
            name = "ACME EOOD",
            representativeName = "John Doe",
            companyIdentifier = "BG123456789",
            address = "1 Main St",
            city = "Sofia",
            postalCode = "1000",
            country = "Bulgaria"
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/clients", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetClient_WhenExists_Returns200WithCorrectFields()
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

        var response = await _client.GetAsync("/api/clients/acme");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("nickname").GetString(), Is.EqualTo("acme"));
        Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("ACME EOOD"));
    }

    [Test]
    public async Task GetClient_WhenNotExists_Returns404()
    {
        var response = await _client.GetAsync("/api/clients/nonexistent");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PutClient_WithValidUpdate_Returns200WithUpdatedFields()
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

        var updateBody = new { name = "ACME Updated" };
        var updateContent = new StringContent(JsonSerializer.Serialize(updateBody), Encoding.UTF8, "application/json");
        var response = await _client.PutAsync("/api/clients/acme", updateContent);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("ACME Updated"));
    }

    [Test]
    public async Task PutClient_WhenNotExists_Returns404()
    {
        var updateBody = new { name = "Updated" };
        var content = new StringContent(JsonSerializer.Serialize(updateBody), Encoding.UTF8, "application/json");
        var response = await _client.PutAsync("/api/clients/nonexistent", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetClients_AfterAddingMultiple_ReturnsAllInList()
    {
        foreach (var nick in new[] { "a", "b", "c" })
        {
            var body = new
            {
                nickname = nick,
                name = $"{nick} Corp",
                representativeName = "Rep",
                companyIdentifier = "BG111",
                address = "1 St",
                city = "Sofia",
                postalCode = "1000",
                country = "BG"
            };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            await _client.PostAsync("/api/clients", content);
        }

        var response = await _client.GetAsync("/api/clients?limit=10");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(3));
    }
}
