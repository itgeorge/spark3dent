using System.Net;
using System.Text;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class InvoicingAuthTests
{
    [Test]
    public async Task InvoicingRoutes_RequireTechnicianAuth()
    {
        using var fixture = new ApiTestFixture();

        using var anonymous = fixture.Client;
        var anonymousResponse = await anonymous.GetAsync("/api/invoicing/clients?limit=1");
        Assert.That(anonymousResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var clinic = fixture.Client;
        await LoginAsync(clinic, "123456");
        var clinicResponse = await clinic.GetAsync("/api/invoicing/clients?limit=1");
        Assert.That(clinicResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var technician = fixture.Client;
        await ApiTestFixture.LoginAsTechnicianAsync(technician);
        var technicianResponse = await technician.GetAsync("/api/invoicing/clients?limit=1");
        Assert.That(technicianResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task LegacyClientAndInvoiceRoutes_AreRetired()
    {
        using var fixture = new ApiTestFixture(autoLoginAsTechnician: true);
        using var client = fixture.Client;

        Assert.That((await client.GetAsync("/api/clients?limit=1")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync("/api/invoices?limit=1")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static async Task LoginAsync(HttpClient client, string pin)
    {
        var response = await client.PostAsync(
            "/api/scheduling/auth/login",
            new StringContent($"{{\"clinicCode\":\"DEMO\",\"pin\":\"{pin}\"}}", Encoding.UTF8, "application/json"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = response.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
    }
}
