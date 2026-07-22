using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class LabOnlyPageAuthTests
{
    [Test]
    public async Task ProtectedAppPages_GivenAnonymous_RedirectToCentralLogin()
    {
        using var fixture = new ApiTestFixture();
        using var client = CreateNoRedirectClient(fixture);

        await AssertRedirectAsync(client, "/orders", "/login?returnUrl=%2Forders");
        await AssertRedirectAsync(client, "/", "/login?returnUrl=%2F");
        await AssertRedirectAsync(client, "/iam", "/login?returnUrl=%2Fiam");
        await AssertRedirectAsync(client, "/scheduling-config", "/login?returnUrl=%2Fscheduling-config");
    }

    [Test]
    public async Task AppPages_GivenClinicSession_AllowOrdersAndFallbackFromLabOnlyPages()
    {
        using var fixture = new ApiTestFixture();
        using var client = CreateNoRedirectClient(fixture);
        await LoginAsClinicAsync(client);

        var orders = await client.GetAsync("/orders");
        Assert.That(orders.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(orders.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));

        await AssertRedirectAsync(client, "/", "/orders");
        await AssertRedirectAsync(client, "/iam", "/orders");
        await AssertRedirectAsync(client, "/scheduling-config", "/orders");
    }

    [Test]
    public async Task AppPages_GivenLabSession_ReturnHtml()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);

        await AssertHtmlContainsAsync(client, "/", "Spark3Dent");
        await AssertHtmlContainsAsync(client, "/orders", "Spark3Dent Поръчки");
        await AssertHtmlContainsAsync(client, "/iam", "Spark3Dent IAM");
        await AssertHtmlContainsAsync(client, "/scheduling-config", "Spark3Dent Scheduling Config");
    }

    [Test]
    public async Task ProductPages_DoNotContainEmbeddedLoginForms()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);

        foreach (var path in new[] { "/", "/orders", "/iam", "/scheduling-config" })
        {
            var html = await (await client.GetAsync(path)).Content.ReadAsStringAsync();
            Assert.That(html, Does.Not.Contain("id=\"login\""), path);
            Assert.That(html, Does.Not.Contain("auth/login"), path);
            Assert.That(html, Does.Not.Contain("id=\"loginBtn\""), path);
        }
    }

    [Test]
    public async Task LoginPage_ReturnsHtml()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;

        var response = await client.GetAsync("/login");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Spark3Dent"));
    }

    [Test]
    public async Task StaticHtmlBypassPaths_DoNotServeAppDocuments()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;

        foreach (var path in new[] { "/iam.html", "/scheduling-config.html", "/orders.html", "/index.html" })
        {
            var response = await client.GetAsync(path);
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK), path);
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.Not.EqualTo("text/html"), path);
        }
    }

    [Test]
    public async Task ReturnUrlResolver_DoesNotHonorUnknownOrExternalTargets()
    {
        using var fixture = new ApiTestFixture();
        using var clinic = CreateNoRedirectClient(fixture);
        await LoginAsClinicAsync(clinic);

        Assert.That(await ResolveReturnUrlAsync(clinic, "https://evil.test/iam"), Is.EqualTo("/orders"));
        Assert.That(await ResolveReturnUrlAsync(clinic, "//evil.test/iam"), Is.EqualTo("/orders"));
        Assert.That(await ResolveReturnUrlAsync(clinic, "/iam"), Is.EqualTo("/orders"));

        using var lab = CreateNoRedirectClient(fixture);
        await ApiTestFixture.LoginAsLabAsync(lab);
        Assert.That(await ResolveReturnUrlAsync(lab, "/iam?tab=members"), Is.EqualTo("/iam?tab=members"));
        Assert.That(await ResolveReturnUrlAsync(lab, "/unknown"), Is.EqualTo("/"));
    }

    private static async Task AssertRedirectAsync(HttpClient client, string path, string location)
    {
        var response = await client.GetAsync(path);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), path);
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo(location), path);
    }

    private static async Task AssertHtmlContainsAsync(HttpClient client, string path, string expected)
    {
        var response = await client.GetAsync(path);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"), path);
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(expected), path);
    }

    private static async Task<string?> ResolveReturnUrlAsync(HttpClient client, string returnUrl)
    {
        var response = await client.GetAsync("/api/app-pages/resolve-return-url?returnUrl=" + Uri.EscapeDataString(returnUrl));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadFromJsonAsync<ResolveReturnUrlResponse>();
        return json?.Path;
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

    private sealed record ResolveReturnUrlResponse(string Path);
}
