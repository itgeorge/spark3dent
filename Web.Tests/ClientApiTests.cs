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
        Assert.That(doc.RootElement.GetProperty("nextStartAfter").ValueKind, Is.EqualTo(JsonValueKind.Null));
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

    [Test]
    public async Task GetClients_WithCursor_ReturnsNextStartAfterAndPaginates()
    {
        foreach (var nick in new[] { "a", "b", "c", "d", "e" })
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

        var first = await _client.GetAsync("/api/clients?limit=2");
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var firstJson = await first.Content.ReadAsStringAsync();
        var firstDoc = JsonDocument.Parse(firstJson);
        var firstItems = firstDoc.RootElement.GetProperty("items");
        Assert.That(firstItems.GetArrayLength(), Is.EqualTo(2));
        Assert.That(firstItems[0].GetProperty("nickname").GetString(), Is.EqualTo("a"));
        Assert.That(firstItems[1].GetProperty("nickname").GetString(), Is.EqualTo("b"));
        var nextStartAfter = firstDoc.RootElement.GetProperty("nextStartAfter").GetString();
        Assert.That(nextStartAfter, Is.EqualTo("b"));

        var second = await _client.GetAsync($"/api/clients?limit=2&startAfter={Uri.EscapeDataString(nextStartAfter!)}");
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var secondJson = await second.Content.ReadAsStringAsync();
        var secondDoc = JsonDocument.Parse(secondJson);
        var secondItems = secondDoc.RootElement.GetProperty("items");
        Assert.That(secondItems.GetArrayLength(), Is.EqualTo(2));
        Assert.That(secondItems[0].GetProperty("nickname").GetString(), Is.EqualTo("c"));
        Assert.That(secondItems[1].GetProperty("nickname").GetString(), Is.EqualTo("d"));
        var nextStartAfter2 = secondDoc.RootElement.GetProperty("nextStartAfter").GetString();
        Assert.That(nextStartAfter2, Is.EqualTo("d"));

        var third = await _client.GetAsync($"/api/clients?limit=2&startAfter={Uri.EscapeDataString(nextStartAfter2!)}");
        Assert.That(third.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var thirdJson = await third.Content.ReadAsStringAsync();
        var thirdDoc = JsonDocument.Parse(thirdJson);
        var thirdItems = thirdDoc.RootElement.GetProperty("items");
        Assert.That(thirdItems.GetArrayLength(), Is.EqualTo(1));
        Assert.That(thirdItems[0].GetProperty("nickname").GetString(), Is.EqualTo("e"));
        var nextStartAfter3 = thirdDoc.RootElement.GetProperty("nextStartAfter").GetString();
        Assert.That(nextStartAfter3, Is.EqualTo("e"));

        var fourth = await _client.GetAsync($"/api/clients?limit=2&startAfter={Uri.EscapeDataString(nextStartAfter3!)}");
        Assert.That(fourth.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var fourthDoc = JsonDocument.Parse(await fourth.Content.ReadAsStringAsync());
        Assert.That(fourthDoc.RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(0));
        Assert.That(fourthDoc.RootElement.GetProperty("nextStartAfter").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task PatchClientRename_WithValidNewNickname_Returns200AndUpdatedClient()
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

        var renameBody = new { newNickname = "acme-corp" };
        var renameContent = new StringContent(JsonSerializer.Serialize(renameBody), Encoding.UTF8, "application/json");
        var response = await _client.PatchAsync("/api/clients/acme/rename", renameContent);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("nickname").GetString(), Is.EqualTo("acme-corp"));
        Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("ACME EOOD"));

        var getOld = await _client.GetAsync("/api/clients/acme");
        Assert.That(getOld.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var getNew = await _client.GetAsync("/api/clients/acme-corp");
        Assert.That(getNew.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PatchClientRename_WhenClientNotExists_Returns404()
    {
        var renameBody = new { newNickname = "newname" };
        var content = new StringContent(JsonSerializer.Serialize(renameBody), Encoding.UTF8, "application/json");
        var response = await _client.PatchAsync("/api/clients/nonexistent/rename", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PatchClientRename_WithEmptyNewNickname_Returns400()
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

        var renameBody = new { newNickname = "" };
        var renameContent = new StringContent(JsonSerializer.Serialize(renameBody), Encoding.UTF8, "application/json");
        var response = await _client.PatchAsync("/api/clients/acme/rename", renameContent);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PatchClientRename_WhenNewNicknameAlreadyExists_Returns409()
    {
        foreach (var nick in new[] { "acme", "beta" })
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

        var renameBody = new { newNickname = "beta" };
        var renameContent = new StringContent(JsonSerializer.Serialize(renameBody), Encoding.UTF8, "application/json");
        var response = await _client.PatchAsync("/api/clients/acme/rename", renameContent);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await ApiTestFixture.AssertJsonErrorAsync(response, HttpStatusCode.Conflict);
    }
}
