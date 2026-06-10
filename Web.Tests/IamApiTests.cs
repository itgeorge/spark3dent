using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class IamApiTests
{
    [Test]
    public async Task IamApi_RequiresLabAuth_AndDoesNotExposePinHashes()
    {
        using var fixture = new ApiTestFixture();

        using var anonymous = fixture.Client;
        Assert.That((await anonymous.GetAsync("/api/iam/organizations")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var clinic = fixture.Client;
        await LoginAsClinicAsync(clinic);
        Assert.That((await clinic.GetAsync("/api/iam/organizations")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var lab = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(lab);
        var list = await lab.GetAsync("/api/iam/organizations?includeInactive=true");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.That(listJson.RootElement.GetProperty("items").GetArrayLength(), Is.GreaterThanOrEqualTo(1));

        var detail = await lab.GetAsync("/api/iam/organizations/DEMO");
        Assert.That(detail.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detailText = await detail.Content.ReadAsStringAsync();
        Assert.That(detailText, Does.Contain("assistant-1"));
        Assert.That(detailText, Does.Not.Contain("pinHash").And.Not.Contain("pbkdf2-sha256"));

        var labDetail = await lab.GetAsync("/api/iam/lab");
        Assert.That(labDetail.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await labDetail.Content.ReadAsStringAsync(), Does.Contain("lab-1"));
    }

    [Test]
    public async Task WebRuntime_DoesNotAutoSeedSchedulingIdentity()
    {
        using var fixture = new ApiTestFixture(seedIdentity: false);
        using var client = fixture.Client;

        var response = await client.PostAsync(
            "/api/scheduling/auth/login",
            Json("{\"organizationCode\":\"LAB\",\"pin\":\"654321\"}"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task IamApi_CanSearchPrefillCreateClinicWithInitialMember_AndAuditWithoutRawSecret()
    {
        using var fixture = new ApiTestFixture();
        using var lab = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(lab);
        await CreateClientAsync(lab, "alpha-client", "Alpha Dental", "ALPHA123", "Sofia");

        var search = await lab.GetAsync("/api/iam/clients?query=alpha&limit=10");
        Assert.That(search.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await search.Content.ReadAsStringAsync(), Does.Contain("alpha-client"));

        var prefill = await lab.GetAsync("/api/iam/clients/alpha-client/prefill");
        Assert.That(prefill.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var prefillText = await prefill.Content.ReadAsStringAsync();
        Assert.That(prefillText, Does.Contain("Alpha Dental"));
        Assert.That(prefillText, Does.Contain("ALPHA-CLIENT"));

        var create = await lab.PostAsync("/api/iam/organizations", Json("""
        {
          "code":"ALPHA-CLINIC",
          "displayName":"Alpha Dental",
          "linkedClientNickname":"alpha-client",
          "displayColor":"#123abc",
          "initialMember":{"id":"front-desk","label":"Front Desk","secret":"custom secret 2026!"}
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createText = await create.Content.ReadAsStringAsync();
        Assert.That(createText, Does.Contain("front-desk"));
        Assert.That(createText, Does.Not.Contain("custom secret 2026!").And.Not.Contain("pbkdf2-sha256"));

        using var clinic = fixture.Client;
        await LoginAsync(clinic, "ALPHA-CLINIC", "custom secret 2026!");
        var me = await clinic.GetAsync("/api/scheduling/auth/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var audit = await lab.GetAsync("/api/invoicing/audit?entityType=Clinic&entityId=ALPHA-CLINIC");
        Assert.That(audit.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var auditText = await audit.Content.ReadAsStringAsync();
        Assert.That(auditText, Does.Contain("ClinicCreated"));
        Assert.That(auditText, Does.Not.Contain("custom secret 2026!"));
    }

    [Test]
    public async Task IamApi_CanEditDeactivateReactivateClinic_AndRevokeClinicSessions()
    {
        using var fixture = new ApiTestFixture();
        using var lab = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(lab);
        await CreateClientAsync(lab, "beta-client", "Beta Dental", "BETA123", "Varna");
        await CreateClinicAsync(lab, "BETA", "Beta Dental", "beta-client", "assistant-1", "Assistant", "123456");

        var update = await lab.PutAsync("/api/iam/organizations/BETA", Json("{\"displayName\":\"Beta Updated\",\"linkedClientNickname\":\"beta-client\",\"displayColor\":\"#0ea5e9\"}"));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await update.Content.ReadAsStringAsync(), Does.Contain("Beta Updated"));

        using var clinic = fixture.Client;
        await LoginAsync(clinic, "BETA", "123456");
        Assert.That((await clinic.GetAsync("/api/scheduling/auth/me")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var deactivate = await lab.DeleteAsync("/api/iam/organizations/BETA");
        Assert.That(deactivate.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await clinic.GetAsync("/api/scheduling/auth/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await TryLoginAsync(fixture, "BETA", "123456")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var inactiveList = await lab.GetAsync("/api/iam/organizations?includeInactive=true");
        Assert.That(await inactiveList.Content.ReadAsStringAsync(), Does.Contain("BETA"));

        var reactivate = await lab.PostAsync("/api/iam/organizations/BETA/reactivate", Json("{}"));
        Assert.That(reactivate.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await TryLoginAsync(fixture, "BETA", "123456")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task IamApi_CanManageMembersAndRotateSecrets()
    {
        using var fixture = new ApiTestFixture();
        using var lab = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(lab);
        await CreateClinicAsync(lab, "GAMMA", "Gamma Dental", null, "assistant-1", "Assistant", "123456");

        var add = await lab.PostAsync("/api/iam/organizations/GAMMA/members", Json("{\"id\":\"assistant-2\",\"label\":\"Assistant 2\",\"secret\":\"custom member secret\"}"));
        Assert.That(add.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(await add.Content.ReadAsStringAsync(), Does.Not.Contain("custom member secret").And.Not.Contain("pbkdf2-sha256"));
        Assert.That((await TryLoginAsync(fixture, "GAMMA", "custom member secret")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var dup = await lab.PostAsync("/api/iam/organizations/GAMMA/members", Json("{\"id\":\"assistant-2\",\"label\":\"Duplicate\",\"secret\":\"123456\"}"));
        Assert.That(dup.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var edit = await lab.PutAsync("/api/iam/organizations/GAMMA/members/assistant-2", Json("{\"label\":\"Updated Assistant\"}"));
        Assert.That(edit.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await edit.Content.ReadAsStringAsync(), Does.Contain("Updated Assistant"));

        using var memberClient = fixture.Client;
        await LoginAsync(memberClient, "GAMMA", "custom member secret");
        var rotate = await lab.PostAsync("/api/iam/organizations/GAMMA/members/assistant-2/secret", Json("{\"secret\":\"new custom secret!\"}"));
        Assert.That(rotate.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await memberClient.GetAsync("/api/scheduling/auth/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await TryLoginAsync(fixture, "GAMMA", "custom member secret")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await TryLoginAsync(fixture, "GAMMA", "new custom secret!")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var memberClient2 = fixture.Client;
        await LoginAsync(memberClient2, "GAMMA", "new custom secret!");
        var deactivate = await lab.DeleteAsync("/api/iam/organizations/GAMMA/members/assistant-2");
        Assert.That(deactivate.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await memberClient2.GetAsync("/api/scheduling/auth/me")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await TryLoginAsync(fixture, "GAMMA", "new custom secret!")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var reactivate = await lab.PostAsync("/api/iam/organizations/GAMMA/members/assistant-2/reactivate", Json("{}"));
        Assert.That(reactivate.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await TryLoginAsync(fixture, "GAMMA", "new custom secret!")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var labMember = await lab.PostAsync("/api/iam/organizations/LAB/members", Json("{\"id\":\"lab-2\",\"label\":\"Lab 2\",\"secret\":\"lab custom secret\"}"));
        Assert.That(labMember.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That((await TryLoginAsync(fixture, "LAB", "lab custom secret")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task IamPage_RendersHtml()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);

        var response = await client.GetAsync("/iam");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Spark3Dent IAM"));
    }

    private static async Task CreateClientAsync(HttpClient client, string nickname, string name, string companyIdentifier, string city)
    {
        var response = await client.PostAsync("/api/invoicing/clients", Json($$"""
        {
          "nickname":"{{nickname}}",
          "name":"{{name}}",
          "companyIdentifier":"{{companyIdentifier}}",
          "address":"Main Street 1",
          "city":"{{city}}",
          "country":"Bulgaria"
        }
        """));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    private static async Task CreateClinicAsync(HttpClient lab, string code, string name, string? linkedClient, string memberId, string memberLabel, string secret)
    {
        var response = await lab.PostAsync("/api/iam/organizations", Json(JsonSerializer.Serialize(new
        {
            code,
            displayName = name,
            linkedClientNickname = linkedClient,
            displayColor = "#123abc",
            initialMember = new { id = memberId, label = memberLabel, secret }
        })));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    private static async Task<HttpResponseMessage> TryLoginAsync(ApiTestFixture fixture, string organizationCode, string secret)
    {
        var client = fixture.CreateClient();
        return await client.PostAsync("/api/scheduling/auth/login", Json(JsonSerializer.Serialize(new { organizationCode, pin = secret })));
    }

    private static async Task LoginAsClinicAsync(HttpClient client) => await LoginAsync(client, "DEMO", "123456");

    private static async Task LoginAsync(HttpClient client, string organizationCode, string secret)
    {
        var response = await client.PostAsync(
            "/api/scheduling/auth/login",
            Json(JsonSerializer.Serialize(new { organizationCode, pin = secret })));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = response.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
