using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Web.Tests;

public class SchedulingApiTests
{
    [Test]
    public async Task SchedulingFlow_LoginCreateListLogout_Works()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;

        var login = await client.PostAsync("/api/scheduling/auth/login", Json("{\"clinicCode\":\"DEMO\",\"pin\":\"123456\"}"));
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = login.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"QA Crown",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":11,
          "toothEnd":11,
          "start":"2026-06-01",
          "end":"2026-06-10"
        }
        """));
        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dateDoc = JsonDocument.Parse(await dates.Content.ReadAsStringAsync());
        Assert.That(dateDoc.RootElement.GetProperty("minimumDate").GetString(), Is.EqualTo("2026-06-05"));

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"QA Crown",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":11,
          "toothEnd":11,
          "shade":"A3.5",
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var orderElement = createDoc.RootElement.GetProperty("order");
        var code = orderElement.GetProperty("orderCode").GetString();
        var shortenedCode = orderElement.GetProperty("shortenedOrderCode").GetString();
        Assert.That(code, Is.Not.Null.And.Contains("-"));
        Assert.That(shortenedCode, Is.EqualTo(code![3..]));
        Assert.That(orderElement.GetProperty("shade").GetString(), Is.EqualTo("A3.5"));

        var list = await client.GetAsync("/api/scheduling/technician/orders");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var listText = await list.Content.ReadAsStringAsync();
        Assert.That(listText, Does.Contain(code));

        var logout = await client.PostAsync("/api/scheduling/auth/logout", Json("{}"));
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SchedulingFlow_RejectsInvalidPinAndUnauthenticatedAccess()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;

        var badLogin = await client.PostAsync("/api/scheduling/auth/login", Json("{\"clinicCode\":\"DEMO\",\"pin\":\"000000\"}"));
        Assert.That(badLogin.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var me = await client.GetAsync("/api/scheduling/auth/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SchedulingFlow_RejectsWeekendDelivery()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Bad Date",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":11,
          "toothEnd":11,
          "requestedDeliveryDate":"2026-06-06"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await create.Content.ReadAsStringAsync(), Does.Contain("Weekend"));
    }

    [Test]
    public async Task SchedulingFlow_NormalizesReversedToothRangeOnCreate()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Reordered Bridge",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"bridge",
          "material":"pfm",
          "constructionType":"bridge",
          "toothStart":22,
          "toothEnd":12,
          "requestedDeliveryDate":"2026-06-09"
        }
        """));

        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var orderElement = createDoc.RootElement.GetProperty("order");
        Assert.Multiple(() =>
        {
            Assert.That(orderElement.GetProperty("toothStart").GetInt32(), Is.EqualTo(12));
            Assert.That(orderElement.GetProperty("toothEnd").GetInt32(), Is.EqualTo(22));
            Assert.That(orderElement.GetProperty("abutmentTeeth").GetString(), Is.EqualTo("12,22"));
        });
    }

    [Test]
    public async Task SchedulingFlow_RejectsToothRangeAcrossBothJaws()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Bad Jaw Range",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"bridge",
          "material":"pfm",
          "constructionType":"bridge",
          "toothStart":28,
          "toothEnd":31,
          "requestedDeliveryDate":"2026-06-09"
        }
        """));

        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await create.Content.ReadAsStringAsync(), Does.Contain("same jaw"));
    }

    [Test]
    public async Task SchedulingFlow_RejectsInvalidToothRangeWhenCalculatingDates()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Bad Jaw Range",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"bridge",
          "material":"pfm",
          "constructionType":"bridge",
          "toothStart":28,
          "toothEnd":31,
          "start":"2026-06-01",
          "end":"2026-06-10"
        }
        """));

        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await dates.Content.ReadAsStringAsync(), Does.Contain("same jaw"));
    }

    [Test]
    public async Task SchedulingConfigReloadEndpoint_IsRemoved()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var response = await client.PostAsync("/api/scheduling/config/reload", Json("{}"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var login = await client.PostAsync("/api/scheduling/auth/login", Json("{\"clinicCode\":\"DEMO\",\"pin\":\"123456\"}"));
        var cookie = login.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
