using System.Net;
using NUnit.Framework;

namespace Web.Tests;

[TestFixture]
public class StartupConfigTests
{
    [Test]
    public void CreateClient_GivenHetznerModeWithoutPort_ThenThrows()
    {
        using var fixture = new ApiTestFixture(runtimeHostingMode: "HetznerDocker", runtimePort: null);

        var ex = Assert.Throws<InvalidOperationException>(() => fixture.CreateClient());
        Assert.That(ex, Is.Not.Null);
        Assert.That(GetFullExceptionMessage(ex!), Does.Contain("Runtime.Port"));
        Assert.That(GetFullExceptionMessage(ex!), Does.Contain("HetznerDocker"));
    }

    [Test]
    public async Task CreateClient_GivenDesktopModeWithoutPort_ThenUsesDynamicPort()
    {
        using var fixture = new ApiTestFixture(runtimeHostingMode: "Desktop", runtimePort: null);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task LicensesPage_ReturnsHtmlWithLicenseEntries()
    {
        using var fixture = new ApiTestFixture(runtimeHostingMode: "Desktop", runtimePort: null);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/licenses");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(html, Does.Contain("Licenses"));
        Assert.That(html, Does.Contain("<details>"));
        Assert.That(html, Does.Contain("HtmlAgilityPack"));
    }

    private static string GetFullExceptionMessage(Exception ex)
    {
        var messages = new List<string>();
        Exception? current = ex;
        while (current != null)
        {
            messages.Add(current.Message);
            current = current.InnerException;
        }

        return string.Join(" | ", messages);
    }
}
