using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class LabOnlyPageAuthTests
{
    [Test]
    public async Task LabOnlyPages_GivenAnonymous_RedirectToOrders()
    {
        using var fixture = new ApiTestFixture();
        using var client = CreateNoRedirectClient(fixture);

        var iam = await client.GetAsync("/iam");
        Assert.That(iam.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(iam.Headers.Location?.ToString(), Is.EqualTo("/orders"));

        var invoicing = await client.GetAsync("/");
        Assert.That(invoicing.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(invoicing.Headers.Location?.ToString(), Is.EqualTo("/orders"));
    }

    [Test]
    public async Task LabOnlyPages_GivenClinicSession_RedirectToOrders()
    {
        using var fixture = new ApiTestFixture();
        using var client = CreateNoRedirectClient(fixture);
        await LoginAsClinicAsync(client);

        var iam = await client.GetAsync("/iam");
        Assert.That(iam.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(iam.Headers.Location?.ToString(), Is.EqualTo("/orders"));

        var invoicing = await client.GetAsync("/");
        Assert.That(invoicing.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(invoicing.Headers.Location?.ToString(), Is.EqualTo("/orders"));
    }

    [Test]
    public async Task LabOnlyPages_GivenLabSession_ReturnHtml()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);

        var iam = await client.GetAsync("/iam");
        Assert.That(iam.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(iam.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(await iam.Content.ReadAsStringAsync(), Does.Contain("Spark3Dent IAM"));

        var invoicing = await client.GetAsync("/");
        Assert.That(invoicing.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(invoicing.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(await invoicing.Content.ReadAsStringAsync(), Does.Contain("Spark3Dent"));
    }

    [Test]
    public async Task OrdersPage_GivenAnonymous_ReturnsHtml()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;

        var response = await client.GetAsync("/orders");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Spark3Dent"));
    }

    private static HttpClient CreateNoRedirectClient(ApiTestFixture fixture) =>
        fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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
