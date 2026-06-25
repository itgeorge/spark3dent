using System.Net;
using Database;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Orders;

namespace Web.Tests;

[TestFixture]
public class ApiHealthTests
{
    [Test]
    public async Task Healthz_ReturnsOk()
    {
        using var fixture = new ApiTestFixture(runtimeHostingMode: "Desktop", runtimePort: null);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        Assert.That(json, Does.Contain("ok"));
    }

    [Test]
    public async Task ServiceProvider_ResolvesNonWorkingDayProvider()
    {
        using var fixture = new ApiTestFixture(runtimeHostingMode: "Desktop", runtimePort: null);
        using var scope = fixture.Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<INonWorkingDayProvider>();
        var days = await provider.GetNonWorkingDaysAsync(2026);

        Assert.That(provider, Is.InstanceOf<DbBackedLabNonWorkingDayProvider>());
        Assert.That(days, Does.Contain(new DateOnly(2026, 3, 3)));
        Assert.That(days, Does.Contain(new DateOnly(2026, 6, 6)));
    }

    [Test]
    public void ServiceProvider_ResolvesDateAvailabilityService()
    {
        using var fixture = new ApiTestFixture(runtimeHostingMode: "Desktop", runtimePort: null);
        using var scope = fixture.Services.CreateScope();

        Assert.DoesNotThrow(() => scope.ServiceProvider.GetRequiredService<DateAvailabilityService>());
    }

    [Test]
    public void ServiceProvider_ResolvesSchedulingOrderService()
    {
        using var fixture = new ApiTestFixture(runtimeHostingMode: "Desktop", runtimePort: null);
        using var scope = fixture.Services.CreateScope();

        Assert.DoesNotThrow(() => scope.ServiceProvider.GetRequiredService<SchedulingOrderService>());
    }

    [Test]
    public async Task SchedulingDatesEndpoint_WhenAuthenticated_ReturnsOk()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.CreateClient();

        var login = await client.PostAsync(
            "/api/scheduling/auth/login",
            new StringContent("{\"clinicCode\":\"DEMO\",\"pin\":\"123456\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = login.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        var response = await client.PostAsync(
            "/api/scheduling/dates",
            new StringContent(
                """
                {
                  "caseName":"Health Check",
                  "impressionDate":"2026-06-02",
                  "productCategory":"permanent",
                  "material":"fullContourZirconia",
                  "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
                  "start":"2026-06-01",
                  "end":"2026-06-10"
                }
                """,
                System.Text.Encoding.UTF8,
                "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }
}
