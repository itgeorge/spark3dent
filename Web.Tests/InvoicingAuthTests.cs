using System.Net;
using System.Text;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class InvoicingAuthTests
{
    [Test]
    public async Task InvoicingRoutes_RequireLabAuth()
    {
        using var fixture = new ApiTestFixture();

        using var anonymous = fixture.Client;
        var anonymousResponse = await anonymous.GetAsync("/api/invoicing/clients?limit=1");
        Assert.That(anonymousResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var clinic = fixture.Client;
        await LoginAsync(clinic, "123456");
        var clinicResponse = await clinic.GetAsync("/api/invoicing/clients?limit=1");
        Assert.That(clinicResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var lab = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(lab);
        var labResponse = await lab.GetAsync("/api/invoicing/clients?limit=1");
        Assert.That(labResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task LegacyClientAndInvoiceRoutes_AreRetired()
    {
        using var fixture = new ApiTestFixture(autoLoginAsLab: true);
        using var client = fixture.Client;

        Assert.That((await client.GetAsync("/api/clients?limit=1")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync("/api/invoices?limit=1")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static async Task LoginAsync(HttpClient client, string pin)
    {
        var response = await client.PostAsync(
            "/api/scheduling/auth/login",
            new StringContent($"{{\"organizationCode\":\"DEMO\",\"pin\":\"{pin}\"}}", Encoding.UTF8, "application/json"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = response.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
    }
}
