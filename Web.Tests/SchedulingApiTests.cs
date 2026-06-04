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

        var list = await client.GetAsync("/api/scheduling/orders");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var listText = await list.Content.ReadAsStringAsync();
        Assert.That(listText, Does.Contain(code));

        var logout = await client.PostAsync("/api/scheduling/auth/logout", Json("{}"));
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SchedulingFlow_UnauthenticatedOrderListReturns401()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;

        var response = await client.GetAsync("/api/scheduling/orders");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SchedulingFlow_TechnicianCanListAndCreateWithTargetClinic()
    {
        using var fixture = new ApiTestFixture();
        using var clinicClient = fixture.Client;
        await LoginAsync(clinicClient);
        var create = await clinicClient.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Clinic Case",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":11,
          "toothEnd":11,
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var code = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("order").GetProperty("orderCode").GetString();

        using var techClient = fixture.Client;
        await ApiTestFixture.LoginAsTechnicianAsync(techClient);
        var list = await techClient.GetAsync("/api/scheduling/orders");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await list.Content.ReadAsStringAsync(), Does.Contain(code));

        var clinics = await techClient.GetAsync("/api/scheduling/clinics");
        Assert.That(clinics.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await clinics.Content.ReadAsStringAsync(), Does.Contain("DEMO"));

        var techCreateMissingClinic = await techClient.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Tech Case",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":11,
          "toothEnd":11,
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(techCreateMissingClinic.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var techCreate = await techClient.PostAsync("/api/scheduling/orders", Json("""
        {
          "clinicCode":"DEMO",
          "caseName":"Tech Case",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":11,
          "toothEnd":11,
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(techCreate.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task SchedulingFlow_UpdateAndCancelOrder_EnforcesPermissions()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);
        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Original Case",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":11,
          "toothEnd":11,
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var code = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("order").GetProperty("orderCode").GetString();

        var update = await client.PutAsync($"/api/scheduling/orders/{code}", Json("""
        {
          "caseName":"Updated Case",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":12,
          "toothEnd":12,
          "requestedDeliveryDate":"2026-06-05",
          "notes":"updated"
        }
        """));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updatedOrder = JsonDocument.Parse(await update.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(updatedOrder.GetProperty("caseName").GetString(), Is.EqualTo("Updated Case"));
        Assert.That(updatedOrder.GetProperty("toothStart").GetInt32(), Is.EqualTo(12));

        var cancel = await client.DeleteAsync($"/api/scheduling/orders/{code}");
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cancelledOrder = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(cancelledOrder.GetProperty("status").GetString(), Is.EqualTo("cancelled"));

        var updateCancelled = await client.PutAsync($"/api/scheduling/orders/{code}", Json("""
        {
          "caseName":"Nope",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "workType":"crown",
          "material":"fullContourZirconia",
          "constructionType":"crown",
          "toothStart":12,
          "toothEnd":12,
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(updateCancelled.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        using var anonymous = fixture.CreateClient();
        var anonymousCancel = await anonymous.DeleteAsync($"/api/scheduling/orders/{code}");
        Assert.That(anonymousCancel.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SchedulingFlow_RetiredTechnicianOrdersRouteReturns404()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await ApiTestFixture.LoginAsTechnicianAsync(client);

        var response = await client.GetAsync("/api/scheduling/technician/orders");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SchedulingFlow_RejectsInvalidPinAndUnauthenticatedAccess()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;

        var badLogin = await client.PostAsync("/api/scheduling/auth/login", Json("{\"clinicCode\":\"DEMO\",\"pin\":\"000000\"}"));
        Assert.That(badLogin.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var badLoginJson = JsonDocument.Parse(await badLogin.Content.ReadAsStringAsync());
        Assert.That(badLoginJson.RootElement.GetProperty("error").GetString(), Is.EqualTo("Invalid credentials."));

        var shapedLogin = await client.PostAsync("/api/scheduling/auth/login", Json("{\"clinicCode\":\"DEMO\",\"pin\":\"abc\"}"));
        Assert.That(shapedLogin.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var shapedLoginJson = JsonDocument.Parse(await shapedLogin.Content.ReadAsStringAsync());
        Assert.That(shapedLoginJson.RootElement.GetProperty("error").GetString(), Is.EqualTo("Invalid credentials."));

        var unknownClinicLogin = await client.PostAsync("/api/scheduling/auth/login", Json("{\"clinicCode\":\"UNKNOWN\",\"pin\":\"abc\"}"));
        Assert.That(unknownClinicLogin.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var unknownClinicLoginJson = JsonDocument.Parse(await unknownClinicLogin.Content.ReadAsStringAsync());
        Assert.That(unknownClinicLoginJson.RootElement.GetProperty("error").GetString(), Is.EqualTo("Invalid credentials."));

        var missingLogin = await client.PostAsync("/api/scheduling/auth/login", Json("{\"clinicCode\":\"DEMO\",\"pin\":\"\"}"));
        Assert.That(missingLogin.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var missingLoginJson = JsonDocument.Parse(await missingLogin.Content.ReadAsStringAsync());
        Assert.That(missingLoginJson.RootElement.GetProperty("error").GetString(), Is.EqualTo("Credentials are required."));

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
