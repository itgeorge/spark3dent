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
    public async Task IamPage_RendersHtml()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;

        var response = await client.GetAsync("/iam");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Spark3Dent IAM"));
    }

    private static async Task LoginAsClinicAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/scheduling/auth/login",
            new StringContent("{\"organizationCode\":\"DEMO\",\"pin\":\"123456\"}", Encoding.UTF8, "application/json"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = response.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
    }
}
