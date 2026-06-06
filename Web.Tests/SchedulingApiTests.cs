using System.Net;
using System.Text;
using System.Text.Json;
using Database;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

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
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
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
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
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
        Assert.That(orderElement.GetProperty("workItems").GetArrayLength(), Is.EqualTo(1));

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
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
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
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
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
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
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
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
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
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "requestedDeliveryDate":"2026-06-05",
          "notes":"updated"
        }
        """));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updatedOrder = JsonDocument.Parse(await update.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(updatedOrder.GetProperty("caseName").GetString(), Is.EqualTo("Updated Case"));
        Assert.That(updatedOrder.GetProperty("workItems")[0].GetProperty("toothStart").GetInt32(), Is.EqualTo(12));
        Assert.That(updatedOrder.TryGetProperty("toothStart", out _), Is.False);
        Assert.That(updatedOrder.TryGetProperty("workType", out _), Is.False);

        var cancel = await client.DeleteAsync($"/api/scheduling/orders/{code}");
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cancelledOrder = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(cancelledOrder.GetProperty("status").GetString(), Is.EqualTo("cancelled"));

        var updateCancelled = await client.PutAsync($"/api/scheduling/orders/{code}", Json("""
        {
          "caseName":"Nope",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(updateCancelled.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        using var anonymous = fixture.CreateClient();
        var anonymousCancel = await anonymous.DeleteAsync($"/api/scheduling/orders/{code}");
        Assert.That(anonymousCancel.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SchedulingCalendarEndpoint_RequiresAuthValidatesRangeAndIsNotCapturedByCodeRoute()
    {
        using var fixture = new ApiTestFixture();
        using var anonymous = fixture.Client;

        var unauthenticated = await anonymous.GetAsync("/api/scheduling/orders/calendar?start=2026-06-01&end=2026-06-30");
        Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var client = fixture.CreateClient();
        await LoginAsync(client);

        var valid = await client.GetAsync("/api/scheduling/orders/calendar?start=2026-06-01&end=2026-06-30");
        Assert.That(valid.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var validDoc = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        Assert.That(validDoc.RootElement.GetProperty("start").GetString(), Is.EqualTo("2026-06-01"));

        var invalid = await client.GetAsync("/api/scheduling/orders/calendar?start=2026-06-30&end=2026-06-01");
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var tooLarge = await client.GetAsync("/api/scheduling/orders/calendar?start=2026-06-01&end=2026-09-30");
        Assert.That(tooLarge.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SchedulingCalendarEndpoint_IsRoleAwareInclusiveAndExcludesCancelled()
    {
        using var fixture = new ApiTestFixture();
        using var clinicClient = fixture.Client;
        await LoginAsync(clinicClient);

        var demoStart = await CreateOrderAsync(clinicClient, "Demo Start", "2026-06-05");
        var demoEndCancelled = await CreateOrderAsync(clinicClient, "Demo Cancelled", "2026-06-10");
        var demoOutside = await CreateOrderAsync(clinicClient, "Demo Outside", "2026-06-12");
        var cancel = await clinicClient.DeleteAsync($"/api/scheduling/orders/{demoEndCancelled}");
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var techClient = fixture.CreateClient();
        await ApiTestFixture.LoginAsTechnicianAsync(techClient);
        var otherEnd = await SeedOrderAsync(fixture.DbPath, "OTH-610", "Other End", "OTHER", "2026-06-10");

        var clinicCalendar = await clinicClient.GetAsync("/api/scheduling/orders/calendar?start=2026-06-05&end=2026-06-10");
        Assert.That(clinicCalendar.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var clinicText = await clinicCalendar.Content.ReadAsStringAsync();
        Assert.That(clinicText, Does.Contain(demoStart));
        Assert.That(clinicText, Does.Not.Contain(demoEndCancelled));
        Assert.That(clinicText, Does.Not.Contain(demoOutside));
        Assert.That(clinicText, Does.Not.Contain(otherEnd));

        var techCalendar = await techClient.GetAsync("/api/scheduling/orders/calendar?start=2026-06-05&end=2026-06-10");
        Assert.That(techCalendar.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var techText = await techCalendar.Content.ReadAsStringAsync();
        Assert.That(techText, Does.Contain(demoStart));
        Assert.That(techText, Does.Contain(otherEnd));
        Assert.That(techText, Does.Not.Contain(demoEndCancelled));

        var list = await clinicClient.GetAsync("/api/scheduling/orders");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await list.Content.ReadAsStringAsync(), Does.Contain(demoEndCancelled));
    }

    [Test]
    public async Task SchedulingFlow_CreateUpdateListGetCalendar_WithOrderWorkItems()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Multi Work Items",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[
            {"constructionType":"bridge","toothStart":11,"toothEnd":13},
            {"constructionType":"crown","toothStart":23,"toothEnd":23}
          ],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        var code = created.GetProperty("orderCode").GetString();
        Assert.That(created.GetProperty("workItems").GetArrayLength(), Is.EqualTo(2));
        Assert.That(created.TryGetProperty("constructionType", out _), Is.False);
        Assert.That(created.TryGetProperty("toothStart", out _), Is.False);
        Assert.That(created.TryGetProperty("abutmentTeeth", out _), Is.False);

        var get = await client.GetAsync($"/api/scheduling/orders/{code}");
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await get.Content.ReadAsStringAsync(), Does.Contain("workItems"));

        var list = await client.GetAsync("/api/scheduling/orders");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await list.Content.ReadAsStringAsync(), Does.Contain("workItems"));

        var calendar = await client.GetAsync("/api/scheduling/orders/calendar?start=2026-06-10&end=2026-06-10");
        Assert.That(calendar.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await calendar.Content.ReadAsStringAsync(), Does.Contain("workItems"));

        var update = await client.PutAsync($"/api/scheduling/orders/{code}", Json("""
        {
          "caseName":"Updated Multi Work Items",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[
            {"constructionType":"bridge","toothStart":11,"toothEnd":13},
            {"constructionType":"crown","toothStart":24,"toothEnd":24}
          ],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updated = JsonDocument.Parse(await update.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(updated.GetProperty("workItems")[1].GetProperty("toothStart").GetInt32(), Is.EqualTo(24));
    }

    [Test]
    public async Task SchedulingFlow_InvalidOverlappingOrderWorkItems_Returns400()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Overlap",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[
            {"constructionType":"bridge","toothStart":11,"toothEnd":13},
            {"constructionType":"crown","toothStart":12,"toothEnd":12}
          ],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));

        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await create.Content.ReadAsStringAsync(), Does.Contain("overlap"));

        var empty = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Empty",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[],
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await empty.Content.ReadAsStringAsync(), Does.Contain("At least one order work item"));
    }

    [Test]
    public async Task SchedulingFlow_OldSingleFieldOnlyRequest_Returns400()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Old Shape",
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

        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await create.Content.ReadAsStringAsync(), Does.Contain("At least one order work item"));
    }

    [Test]
    public async Task SchedulingDates_GivenMultipleOrderWorkItems_UsesSummedLeadTime()
    {
        using var fixture = new ApiTestFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Multi dates",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[
            {"constructionType":"bridge","toothStart":11,"toothEnd":13},
            {"constructionType":"crown","toothStart":23,"toothEnd":23}
          ],
          "start":"2026-06-01",
          "end":"2026-06-15"
        }
        """));

        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await dates.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.GetProperty("minimumDate").GetString(), Is.EqualTo("2026-06-10"));
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
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
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
          "material":"pfm",
          "workItems":[{"constructionType":"bridge","toothStart":22,"toothEnd":12}],
          "requestedDeliveryDate":"2026-06-09"
        }
        """));

        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var orderElement = createDoc.RootElement.GetProperty("order");
        Assert.Multiple(() =>
        {
            Assert.That(orderElement.GetProperty("workItems")[0].GetProperty("toothStart").GetInt32(), Is.EqualTo(12));
            Assert.That(orderElement.GetProperty("workItems")[0].GetProperty("toothEnd").GetInt32(), Is.EqualTo(22));
            Assert.That(orderElement.TryGetProperty("abutmentTeeth", out _), Is.False);
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
          "material":"pfm",
          "workItems":[{"constructionType":"bridge","toothStart":28,"toothEnd":31}],
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
          "material":"pfm",
          "workItems":[{"constructionType":"bridge","toothStart":28,"toothEnd":31}],
          "start":"2026-06-01",
          "end":"2026-06-10"
        }
        """));

        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await dates.Content.ReadAsStringAsync(), Does.Contain("same jaw"));
    }

    [Test]
    public async Task SchedulingOrdersListEndpoint_ReturnsCursorPageAndRejectsInvalidCursor()
    {
        using var fixture = new ApiTestFixture();
        await SeedOrderAsync(fixture.DbPath, "PG1-234", "Page old", "DEMO", "2026-06-05");
        await SeedOrderAsync(fixture.DbPath, "PG2-234", "Page mid", "DEMO", "2026-06-06");
        await SeedOrderAsync(fixture.DbPath, "PG3-234", "Page new", "DEMO", "2026-06-07");
        using var client = fixture.Client;
        await LoginAsync(client);

        var first = await client.GetAsync("/api/scheduling/orders?limit=2");
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.That(firstDoc.RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
        Assert.That(firstDoc.RootElement.GetProperty("hasMore").GetBoolean(), Is.True);
        var cursor = firstDoc.RootElement.GetProperty("nextCursor").GetString();
        Assert.That(cursor, Is.Not.Null.And.Not.Empty);

        var second = await client.GetAsync($"/api/scheduling/orders?limit=2&cursor={Uri.EscapeDataString(cursor!)}");
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.That(secondDoc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("orderCode").GetString()), Does.Contain("PG1-234"));

        var invalid = await client.GetAsync("/api/scheduling/orders?cursor=bad-cursor");
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SchedulingOrdersFindEndpoint_ReturnsContextAndEnforcesVisibility()
    {
        using var fixture = new ApiTestFixture();
        await SeedOrderAsync(fixture.DbPath, "26-0605-Z1AA", "Find demo", "DEMO", "2026-06-05");
        var cancelledCode = await SeedOrderAsync(fixture.DbPath, "26-0606-Z1BB", "Find cancelled", "DEMO", "2026-06-06");
        await SeedOrderAsync(fixture.DbPath, "27-0605-Z1AA", "Find other", "OTHER", "2027-06-05");

        using var clinicClient = fixture.Client;
        await LoginAsync(clinicClient);
        var cancel = await clinicClient.DeleteAsync($"/api/scheduling/orders/{cancelledCode}");
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var findShort = await clinicClient.GetAsync("/api/scheduling/orders/find?code=0605-Z1AA&limit=2");
        Assert.That(findShort.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var findDoc = JsonDocument.Parse(await findShort.Content.ReadAsStringAsync());
        Assert.That(findDoc.RootElement.GetProperty("order").GetProperty("orderCode").GetString(), Is.EqualTo("26-0605-Z1AA"));
        Assert.That(findDoc.RootElement.GetProperty("listPage").GetProperty("items").EnumerateArray().Select(e => e.GetProperty("orderCode").GetString()), Does.Contain("26-0605-Z1AA"));

        var otherFull = await clinicClient.GetAsync("/api/scheduling/orders/find?code=27-0605-Z1AA");
        Assert.That(otherFull.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var cancelled = await clinicClient.GetAsync($"/api/scheduling/orders/find?code={cancelledCode}");
        Assert.That(cancelled.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cancelledDoc = JsonDocument.Parse(await cancelled.Content.ReadAsStringAsync());
        Assert.That(cancelledDoc.RootElement.GetProperty("listModeRecommended").GetBoolean(), Is.True);

        using var techClient = fixture.CreateClient();
        await ApiTestFixture.LoginAsTechnicianAsync(techClient);
        var techFind = await techClient.GetAsync("/api/scheduling/orders/find?code=27-0605-Z1AA");
        Assert.That(techFind.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var ambiguousShort = await techClient.GetAsync("/api/scheduling/orders/find?code=0605-Z1AA");
        Assert.That(ambiguousShort.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
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

    private static async Task<string> CreateOrderAsync(HttpClient client, string caseName, string requestedDeliveryDate, string? clinicCode = null)
    {
        var clinicPrefix = clinicCode == null ? "" : $"\"clinicCode\":\"{clinicCode}\",";
        var create = await client.PostAsync("/api/scheduling/orders", Json($$"""
        {
          {{clinicPrefix}}
          "caseName":"{{caseName}}",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"{{requestedDeliveryDate}}"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("order").GetProperty("orderCode").GetString()!;
    }

    private static async Task<string> SeedOrderAsync(string dbPath, string code, string caseName, string clinicCode, string requestedDeliveryDate)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using (var ctx = new AppDbContext(options))
            await ctx.Database.MigrateAsync();
        var repo = new SqliteOrderRepo(() => new AppDbContext(options));
        await repo.CreateOrderAsync(new OrderRecord(
            0,
            code,
            clinicCode,
            clinicCode == "DEMO" ? "Demo Dental Clinic" : "Other Clinic",
            "seed",
            "Seed",
            "fingerprint",
            caseName,
            new DateOnly(2026, 6, 2),
            ProductCategory.Permanent,
            Material.FullContourZirconia,
            [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
            DateOnly.Parse(requestedDeliveryDate),
            OrderStatus.Created,
            Shade.Unspecified,
            null,
            DateTimeOffset.Parse("2026-06-02T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-02T12:00:00Z"),
            "127.0.0.1",
            "test"));
        return code;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
