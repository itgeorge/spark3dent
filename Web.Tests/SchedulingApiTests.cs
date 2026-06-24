using System.Net;
using System.Text;
using System.Text.Json;
using Database;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;
using Utilities;

namespace Web.Tests;

public class SchedulingApiTests
{
    private static ApiTestFixture NewSchedulingFixture(DateTimeOffset? utcNow = null) =>
        new(clockOverride: new FixedSchedulingClock(utcNow ?? new DateTimeOffset(2026, 6, 2, 7, 30, 0, TimeSpan.Zero)));

    private sealed class FixedSchedulingClock : IClock
    {
        public FixedSchedulingClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.Date);
    }

    [Test]
    public async Task SchedulingFlow_LoginCreateListLogout_Works()
    {
        using var fixture = NewSchedulingFixture();
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
        var dateStatuses = dateDoc.RootElement.GetProperty("dates").EnumerateArray()
            .ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.That(dateStatuses["2026-06-04"].GetProperty("isBeforeMinimum").GetBoolean(), Is.True);
        Assert.That(dateStatuses["2026-06-04"].GetProperty("isSelectable").GetBoolean(), Is.False);
        Assert.That(dateStatuses["2026-06-05"].GetProperty("isSelectable").GetBoolean(), Is.True);
        Assert.That(dateStatuses["2026-06-08"].GetProperty("isFirstBusinessDayAfterClosure").GetBoolean(), Is.True);
        Assert.That(dateStatuses["2026-06-08"].GetProperty("isSelectable").GetBoolean(), Is.False);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"QA Crown",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "shade":"A3.5",
          "colorNote":"incisal translucent, cervical A3.5",
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
        Assert.That(orderElement.GetProperty("colorNote").GetString(), Is.EqualTo("incisal translucent, cervical A3.5"));
        Assert.That(orderElement.GetProperty("workItems").GetArrayLength(), Is.EqualTo(1));

        var list = await client.GetAsync("/api/scheduling/orders");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var listText = await list.Content.ReadAsStringAsync();
        Assert.That(listText, Does.Contain(code));
        Assert.That(listText, Does.Contain("incisal translucent, cervical A3.5"));

        var logout = await client.PostAsync("/api/scheduling/auth/logout", Json("{}"));
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SchedulingDates_AndClinicCreate_BlockFirstBusinessDayAfterWeekend()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 2, 25, 7, 30, 0, TimeSpan.Zero));
        using var client = fixture.Client;
        await LoginAsync(client);

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"After Weekend",
          "impressionDate":"2026-02-25",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "start":"2026-03-02",
          "end":"2026-03-05"
        }
        """));
        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var statuses = JsonDocument.Parse(await dates.Content.ReadAsStringAsync()).RootElement.GetProperty("dates").EnumerateArray().ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.Multiple(() =>
        {
            Assert.That(statuses["2026-03-02"].GetProperty("isFirstBusinessDayAfterClosure").GetBoolean(), Is.True);
            Assert.That(statuses["2026-03-02"].GetProperty("isSelectable").GetBoolean(), Is.False);
            Assert.That(statuses["2026-03-02"].GetProperty("reason").GetString(), Is.EqualTo("First business day after weekend/closure"));
            Assert.That(statuses["2026-03-03"].GetProperty("isClosed").GetBoolean(), Is.True);
            Assert.That(statuses["2026-03-04"].GetProperty("isFirstBusinessDayAfterClosure").GetBoolean(), Is.True);
            Assert.That(statuses["2026-03-05"].GetProperty("isSelectable").GetBoolean(), Is.True);
        });

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Blocked Monday",
          "impressionDate":"2026-02-25",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-03-02"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await create.Content.ReadAsStringAsync(), Does.Contain("First business day after weekend/closure"));
    }

    [Test]
    public async Task SchedulingDates_AndClinicCreate_BlockFirstBusinessDayAfterHoliday()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 2, 25, 7, 30, 0, TimeSpan.Zero));
        using var client = fixture.Client;
        await LoginAsync(client);

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"After Holiday",
          "impressionDate":"2026-02-25",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "start":"2026-03-03",
          "end":"2026-03-05"
        }
        """));
        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var statuses = JsonDocument.Parse(await dates.Content.ReadAsStringAsync()).RootElement.GetProperty("dates").EnumerateArray().ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.Multiple(() =>
        {
            Assert.That(statuses["2026-03-03"].GetProperty("isClosed").GetBoolean(), Is.True);
            Assert.That(statuses["2026-03-04"].GetProperty("isFirstBusinessDayAfterClosure").GetBoolean(), Is.True);
            Assert.That(statuses["2026-03-04"].GetProperty("isSelectable").GetBoolean(), Is.False);
            Assert.That(statuses["2026-03-04"].GetProperty("reason").GetString(), Is.EqualTo("First business day after weekend/closure"));
            Assert.That(statuses["2026-03-05"].GetProperty("isSelectable").GetBoolean(), Is.True);
        });

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Blocked After Holiday",
          "impressionDate":"2026-02-25",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-03-04"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await create.Content.ReadAsStringAsync(), Does.Contain("First business day after weekend/closure"));
    }

    [Test]
    public async Task SchedulingConfig_ReturnsDbBackedMaterialSchedulingConfig()
    {
        using var fixture = NewSchedulingFixture();
        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);

        var response = await client.GetAsync("/api/scheduling/config");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var configs = doc.RootElement.GetProperty("materialSchedulingConfigs").EnumerateArray().ToArray();
        var capacityConfigs = doc.RootElement.GetProperty("capacityConfigs").EnumerateArray().ToArray();
        Assert.That(capacityConfigs, Is.Not.Empty);
        var pfm = configs.Single(c => c.GetProperty("material").GetString() == "pfm");
        Assert.Multiple(() =>
        {
            Assert.That(pfm.GetProperty("fixedLeadTimeBusinessDays").GetInt32(), Is.EqualTo(4));
            Assert.That(pfm.GetProperty("capacityUnitsPerTooth").GetDecimal(), Is.EqualTo(1.0m));
            Assert.That(pfm.GetProperty("teethPerExtraLeadDay").GetInt32(), Is.EqualTo(10));
            Assert.That(doc.RootElement.GetProperty("materialOptions").EnumerateArray().Any(x => x.GetProperty("material").GetString() == "pfm" && x.GetProperty("title").GetString() == "Metal-ceramic"), Is.True);
        });
    }

    [Test]
    public async Task SchedulingConfigAdmin_WriteEndpointsRequireLabAndAuditChanges()
    {
        using var fixture = NewSchedulingFixture();
        using var unauthenticated = fixture.Client;

        var unauthenticatedWrite = await unauthenticated.PostAsync("/api/scheduling/config/capacity", Json("{\"activeFromDate\":\"2026-12-01\",\"dailyCapacityUnits\":30,\"weeklyCapacityUnits\":150}"));
        Assert.That(unauthenticatedWrite.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var clinicClient = fixture.CreateClient();
        await LoginAsync(clinicClient);
        var clinicWrite = await clinicClient.PostAsync("/api/scheduling/config/capacity", Json("{\"activeFromDate\":\"2026-12-01\",\"dailyCapacityUnits\":30,\"weeklyCapacityUnits\":150}"));
        Assert.That(clinicWrite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var labClient = fixture.CreateClient();
        await ApiTestFixture.LoginAsLabAsync(labClient);
        var create = await labClient.PostAsync("/api/scheduling/config/capacity", Json("{\"activeFromDate\":\"2026-12-01\",\"dailyCapacityUnits\":30,\"weeklyCapacityUnits\":150}"));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("capacityConfig");
        var id = created.GetProperty("id").GetInt64();

        var removedUpdate = await labClient.PutAsync($"/api/scheduling/config/capacity/{id}", Json("{\"dailyCapacityUnits\":35,\"weeklyCapacityUnits\":175}"));
        Assert.That(removedUpdate.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var materialUpdate = await labClient.PutAsync("/api/scheduling/config/materials/pmma", Json("{\"fixedLeadTimeBusinessDays\":2,\"capacityUnitsPerTooth\":1.25,\"teethPerExtraLeadDay\":null}"));
        Assert.That(materialUpdate.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var get = await labClient.GetAsync("/api/scheduling/config");
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var capacity = doc.RootElement.GetProperty("capacityConfigs").EnumerateArray().Single(c => c.GetProperty("id").GetInt64() == id);
        var pmma = doc.RootElement.GetProperty("materialSchedulingConfigs").EnumerateArray().Single(c => c.GetProperty("material").GetString() == "pmma");
        Assert.Multiple(() =>
        {
            Assert.That(capacity.GetProperty("dailyCapacityUnits").GetDecimal(), Is.EqualTo(30m));
            Assert.That(capacity.GetProperty("weeklyCapacityUnits").GetDecimal(), Is.EqualTo(150m));
            Assert.That(pmma.GetProperty("capacityUnitsPerTooth").GetDecimal(), Is.EqualTo(1.25m));
            Assert.That(pmma.GetProperty("activeFromDate").GetString(), Is.EqualTo("2026-06-02"));
        });

        var history = await labClient.GetAsync("/api/scheduling/config/materials/pmma/history?limit=10");
        Assert.That(history.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var historyItems = JsonDocument.Parse(await history.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.That(historyItems.Select(i => i.GetProperty("activeFromDate").GetString()), Is.EqualTo(new[] { "2026-06-02", "2026-01-01" }));

        await using var ctx = OpenDb(fixture.DbPath);
        var operations = await ctx.AuditEvents.AsNoTracking().Select(e => e.Operation).ToArrayAsync();
        Assert.That(operations, Does.Contain("SchedulingCapacityConfigCreated"));
        Assert.That(operations, Does.Contain("SchedulingMaterialConfigUpdated"));
    }

    [Test]
    public async Task SchedulingConfigAdmin_RejectsInvalidValues()
    {
        using var fixture = NewSchedulingFixture();
        using var labClient = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(labClient);

        var badCapacity = await labClient.PostAsync("/api/scheduling/config/capacity", Json("{\"activeFromDate\":\"2026-12-01\",\"dailyCapacityUnits\":0,\"weeklyCapacityUnits\":150}"));
        Assert.That(badCapacity.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var badMaterial = await labClient.PutAsync("/api/scheduling/config/materials/pfm", Json("{\"fixedLeadTimeBusinessDays\":4,\"capacityUnitsPerTooth\":1,\"teethPerExtraLeadDay\":null}"));
        Assert.That(badMaterial.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SchedulingConfigAdmin_ChangedCapacityBlocksClinicOrderOverDailyLimit()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        using var labClient = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(labClient);

        var createCapacity = await labClient.PostAsync("/api/scheduling/config/capacity", Json("{\"activeFromDate\":\"2026-06-08\",\"dailyCapacityUnits\":30,\"weeklyCapacityUnits\":1000}"));
        Assert.That(createCapacity.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var updateMaterial = await labClient.PutAsync("/api/scheduling/config/materials/pmma", Json("{\"fixedLeadTimeBusinessDays\":2,\"capacityUnitsPerTooth\":1.0,\"teethPerExtraLeadDay\":null}"));
        Assert.That(updateMaterial.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var clinicClient = fixture.CreateClient();
        await LoginAsync(clinicClient);
        var create = await clinicClient.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Thirty One Teeth",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[
            {"constructionType":"bridge","toothStart":18,"toothEnd":28},
            {"constructionType":"bridge","toothStart":48,"toothEnd":37}
          ],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));

        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var error = await create.Content.ReadAsStringAsync();
        Assert.That(error, Does.Contain("Daily capacity exceeded"));
        Assert.That(error, Does.Contain("DailyCapacityExceeded"));
    }

    [Test]
    public async Task SchedulingMaterialOptionsEndpoint_ReportsMissingConfigMaterials()
    {
        using var fixture = NewSchedulingFixture();
        await RemoveMaterialConfigRowsAsync(fixture.DbPath, Material.PmmaTelio);
        using var client = fixture.Client;
        await LoginAsync(client);

        var response = await client.GetAsync("/api/scheduling/material-options");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray().ToArray();
        var pmmaTelio = items.Single(i => i.GetProperty("material").GetString() == "pmmaTelio");
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(i => i.GetProperty("material").GetString()), Is.EqualTo(new[] { "fullContourZirconia", "pfzLayeredZrCrown", "pfm", "glassCeramics", "pmma", "pmmaTelio" }));
            Assert.That(pmmaTelio.GetProperty("title").GetString(), Is.EqualTo("PMMA Telio"));
            Assert.That(pmmaTelio.GetProperty("hasAnyConfig").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task SchedulingConfigAdmin_CanAddMissingMaterial()
    {
        using var fixture = NewSchedulingFixture();
        await RemoveMaterialConfigRowsAsync(fixture.DbPath, Material.PmmaTelio);
        using var labClient = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(labClient);

        var before = await labClient.GetAsync("/api/scheduling/config");
        var beforeDoc = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
        Assert.That(beforeDoc.RootElement.GetProperty("missingMaterials").EnumerateArray().Select(x => x.GetProperty("material").GetString()), Does.Contain("pmmaTelio"));

        var create = await labClient.PostAsync("/api/scheduling/config/materials", Json("{\"material\":\"pmmaTelio\",\"fixedLeadTimeBusinessDays\":2,\"capacityUnitsPerTooth\":1.1,\"teethPerExtraLeadDay\":null}"));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var after = await labClient.GetAsync("/api/scheduling/config");
        var afterDoc = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.That(afterDoc.RootElement.GetProperty("missingMaterials").EnumerateArray().Select(x => x.GetProperty("material").GetString()), Does.Not.Contain("pmmaTelio"));
        var current = afterDoc.RootElement.GetProperty("materialSchedulingConfigs").EnumerateArray().Single(x => x.GetProperty("material").GetString() == "pmmaTelio");
        Assert.That(current.GetProperty("capacityUnitsPerTooth").GetDecimal(), Is.EqualTo(1.1m));
    }

    [Test]
    public async Task SchedulingDates_ReflectDbEditedMaterialLeadTime()
    {
        using var fixture = NewSchedulingFixture();
        await UpdateMaterialConfigAsync(fixture.DbPath, Material.Pmma, fixedLeadTimeBusinessDays: 3);
        using var client = fixture.Client;
        await LoginAsync(client);

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"DB PMMA",
          "impressionDate":"2026-06-02",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "start":"2026-06-01",
          "end":"2026-06-10"
        }
        """));

        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await dates.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.GetProperty("minimumDate").GetString(), Is.EqualTo("2026-06-05"));
    }

    [Test]
    public async Task SchedulingFlow_CreateValidationReflectsDbEditedMaterialLeadTime()
    {
        using var fixture = NewSchedulingFixture();
        await UpdateMaterialConfigAsync(fixture.DbPath, Material.FullContourZirconia, fixedLeadTimeBusinessDays: 4);
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"DB Validation",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-06-05"
        }
        """));

        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await create.Content.ReadAsStringAsync(), Does.Contain("Before minimum lead time"));
    }

    [Test]
    public async Task SchedulingFlow_CapacityAwareCreateRejectAndCancelRelease_WorkEndToEnd()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 1.0m, 10.0m);
        using var client = fixture.Client;
        await LoginAsync(client);

        var firstCreate = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Capacity First",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));
        Assert.That(firstCreate.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var firstDoc = JsonDocument.Parse(await firstCreate.Content.ReadAsStringAsync());
        var firstOrder = firstDoc.RootElement.GetProperty("order");
        var firstCode = firstOrder.GetProperty("orderCode").GetString()!;
        Assert.That(firstOrder.GetProperty("calculatedCapacityUnits").GetDecimal(), Is.EqualTo(1.0m));

        await using (var ctx = OpenDb(fixture.DbPath))
        {
            var dbOrder = await ctx.SchedulingOrders.SingleAsync(o => o.OrderCode == firstCode);
            Assert.That(dbOrder.CalculatedCapacityUnits, Is.EqualTo(1.0m));
        }

        var datesFull = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Capacity Second",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "start":"2026-06-10",
          "end":"2026-06-11"
        }
        """));
        Assert.That(datesFull.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var fullStatuses = JsonDocument.Parse(await datesFull.Content.ReadAsStringAsync()).RootElement.GetProperty("dates").EnumerateArray().ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.That(fullStatuses["2026-06-10"].GetProperty("isSelectable").GetBoolean(), Is.False);
        Assert.That(fullStatuses["2026-06-10"].GetProperty("reason").GetString(), Is.EqualTo("Daily capacity exceeded"));
        Assert.That(fullStatuses["2026-06-11"].GetProperty("isSelectable").GetBoolean(), Is.True);

        var rejected = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Capacity Reject",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await rejected.Content.ReadAsStringAsync(), Does.Contain("Daily capacity exceeded"));

        var secondCreate = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Capacity Second",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "requestedDeliveryDate":"2026-06-11"
        }
        """));
        Assert.That(secondCreate.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var secondOrder = JsonDocument.Parse(await secondCreate.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(secondOrder.GetProperty("calculatedCapacityUnits").GetDecimal(), Is.EqualTo(1.0m));

        var cancel = await client.DeleteAsync($"/api/scheduling/orders/{firstCode}");
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var datesReleased = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Capacity Third",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":13,"toothEnd":13}],
          "start":"2026-06-10",
          "end":"2026-06-11"
        }
        """));
        Assert.That(datesReleased.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var releasedStatuses = JsonDocument.Parse(await datesReleased.Content.ReadAsStringAsync()).RootElement.GetProperty("dates").EnumerateArray().ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.That(releasedStatuses["2026-06-10"].GetProperty("isSelectable").GetBoolean(), Is.True);
    }

    [Test]
    public async Task SchedulingDates_WeeklyCapacityFullCandidateUnavailableAndNextWeekSelectable()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 10.0m, 1.0m);
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Weekly Full",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Weekly Second",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "start":"2026-06-10",
          "end":"2026-06-16"
        }
        """));
        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var statuses = JsonDocument.Parse(await dates.Content.ReadAsStringAsync()).RootElement.GetProperty("dates").EnumerateArray().ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.That(statuses["2026-06-10"].GetProperty("isSelectable").GetBoolean(), Is.False);
        Assert.That(statuses["2026-06-10"].GetProperty("reason").GetString(), Is.EqualTo("Weekly capacity exceeded"));
        Assert.That(statuses["2026-06-11"].GetProperty("isSelectable").GetBoolean(), Is.False);
        Assert.That(statuses["2026-06-16"].GetProperty("isSelectable").GetBoolean(), Is.True);
    }

    [Test]
    public async Task SchedulingDates_DailyCapacityExceeded_WhenExistingUsageIs11AndNewOrderIs2_ShowsWarningAndRejectsClinicCreate()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 12.0m, 100.0m);
        await UpdateMaterialConfigAsync(fixture.DbPath, Material.FullContourZirconia, capacityUnitsPerTooth: 1.0m);
        using var client = fixture.Client;
        await LoginAsync(client);

        var existing = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Existing Eleven Units",
          "impressionDate":"2026-06-08",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"bridge","toothStart":18,"toothEnd":23}],
          "requestedDeliveryDate":"2026-06-11"
        }
        """));
        Assert.That(existing.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"New Two Units",
          "impressionDate":"2026-06-08",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"bridge","toothStart":24,"toothEnd":25}],
          "start":"2026-06-11",
          "end":"2026-06-12"
        }
        """));
        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var datesDoc = JsonDocument.Parse(await dates.Content.ReadAsStringAsync());
        var statuses = datesDoc.RootElement.GetProperty("dates").EnumerateArray().ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.That(datesDoc.RootElement.GetProperty("recommendedDate").GetString(), Is.EqualTo("2026-06-12"));
        Assert.That(statuses["2026-06-11"].GetProperty("isSelectable").GetBoolean(), Is.False);
        Assert.That(statuses["2026-06-11"].GetProperty("reason").GetString(), Is.EqualTo("Daily capacity exceeded"));

        var rejected = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Rejected Two Units",
          "impressionDate":"2026-06-08",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"bridge","toothStart":24,"toothEnd":25}],
          "requestedDeliveryDate":"2026-06-11"
        }
        """));
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await rejected.Content.ReadAsStringAsync(), Does.Contain("Daily capacity exceeded"));
    }

    [Test]
    public async Task SchedulingDates_DailyCapacityExceeded_WhenSingleOrderWouldBe13AgainstCap12_ShowsWarningAndRejectsClinicCreate()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 12.0m, 100.0m);
        await UpdateMaterialConfigAsync(fixture.DbPath, Material.FullContourZirconia, capacityUnitsPerTooth: 1.0m);
        using var client = fixture.Client;
        await LoginAsync(client);

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Thirteen Units",
          "impressionDate":"2026-06-08",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"bridge","toothStart":18,"toothEnd":25}],
          "start":"2026-06-11",
          "end":"2026-06-12"
        }
        """));
        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var datesDoc = JsonDocument.Parse(await dates.Content.ReadAsStringAsync());
        var statuses = datesDoc.RootElement.GetProperty("dates").EnumerateArray().ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.That(datesDoc.RootElement.GetProperty("recommendedDate").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(statuses["2026-06-11"].GetProperty("isSelectable").GetBoolean(), Is.False);
        Assert.That(statuses["2026-06-11"].GetProperty("reason").GetString(), Is.EqualTo("Daily capacity exceeded"));

        var rejected = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Rejected Thirteen Units",
          "impressionDate":"2026-06-08",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"bridge","toothStart":18,"toothEnd":25}],
          "requestedDeliveryDate":"2026-06-11"
        }
        """));
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await rejected.Content.ReadAsStringAsync(), Does.Contain("Daily capacity exceeded"));
    }

    [Test]
    public async Task SchedulingDates_ReturnsRecommendedDateOutsideVisibleMonth_WhenEarliestSelectableIsNextMonth()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 30, 7, 30, 0, TimeSpan.Zero));
        using var client = fixture.Client;
        await LoginAsync(client);

        var dates = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Month Jump",
          "impressionDate":"2026-06-30",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "start":"2026-06-01",
          "end":"2026-06-30"
        }
        """));

        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await dates.Content.ReadAsStringAsync());
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.GetProperty("minimumDate").GetString(), Is.EqualTo("2026-07-02"));
            Assert.That(doc.RootElement.GetProperty("recommendedDate").GetString(), Is.EqualTo("2026-07-02"));
            Assert.That(doc.RootElement.GetProperty("dates").EnumerateArray().All(x => x.GetProperty("isSelectable").GetBoolean() == false), Is.True);
        });
    }

    [Test]
    public async Task SchedulingFlow_UpdateExcludesSelfButRejectsMoveOntoFullDate()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 1.0m, 10.0m);
        using var client = fixture.Client;
        await LoginAsync(client);

        var firstCode = await CreateOrderAsync(client, "Update Self", "2026-06-10", material: "pmma", productCategory: "temporary");
        var secondCode = await CreateOrderAsync(client, "Update Other", "2026-06-11", material: "pmma", productCategory: "temporary");

        var selfUpdate = await client.PutAsync($"/api/scheduling/orders/{firstCode}", Json("""
        {
          "caseName":"Update Self Renamed",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));
        Assert.That(selfUpdate.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var blockedMove = await client.PutAsync($"/api/scheduling/orders/{secondCode}", Json("""
        {
          "caseName":"Update Other Blocked",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "requestedDeliveryDate":"2026-06-10"
        }
        """));
        Assert.That(blockedMove.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await blockedMove.Content.ReadAsStringAsync(), Does.Contain("Daily capacity exceeded"));

        var reloaded = await client.GetAsync($"/api/scheduling/orders/{secondCode}");
        Assert.That(reloaded.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var reloadedOrder = JsonDocument.Parse(await reloaded.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(reloadedOrder.GetProperty("requestedDeliveryDate").GetString(), Is.EqualTo("2026-06-11"));
    }

    [Test]
    public async Task SchedulingFlow_ConcurrentCreatesCannotOverbookDailyCapacity()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 1.0m, 10.0m);
        using var client1 = fixture.CreateClient();
        using var client2 = fixture.CreateClient();
        await LoginAsync(client1);
        await LoginAsync(client2);
        using var barrier = new Barrier(2);

        var tasks = new[]
        {
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await client1.PostAsync("/api/scheduling/orders", Json("""
                {
                  "caseName":"Concurrent Daily A",
                  "impressionDate":"2026-06-08",
                  "productCategory":"temporary",
                  "material":"pmma",
                  "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
                  "requestedDeliveryDate":"2026-06-10"
                }
                """));
            }),
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await client2.PostAsync("/api/scheduling/orders", Json("""
                {
                  "caseName":"Concurrent Daily B",
                  "impressionDate":"2026-06-08",
                  "productCategory":"temporary",
                  "material":"pmma",
                  "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
                  "requestedDeliveryDate":"2026-06-10"
                }
                """));
            })
        };

        var responses = await Task.WhenAll(tasks);
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Created), Is.EqualTo(1));
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest), Is.EqualTo(1));
        var rejected = responses.Single(r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.That(await rejected.Content.ReadAsStringAsync(), Does.Contain("Daily capacity exceeded"));

        await using var ctx = OpenDb(fixture.DbPath);
        var activeCount = await ctx.SchedulingOrders.CountAsync(o => o.Status != nameof(OrderStatus.Cancelled) && o.RequestedDeliveryDate == new DateOnly(2026, 6, 10));
        Assert.That(activeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SchedulingFlow_ConcurrentCreatesCannotOverbookWeeklyCapacity()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 10.0m, 1.0m);
        using var client1 = fixture.CreateClient();
        using var client2 = fixture.CreateClient();
        await LoginAsync(client1);
        await LoginAsync(client2);
        using var barrier = new Barrier(2);

        var tasks = new[]
        {
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await client1.PostAsync("/api/scheduling/orders", Json("""
                {
                  "caseName":"Concurrent Weekly A",
                  "impressionDate":"2026-06-08",
                  "productCategory":"temporary",
                  "material":"pmma",
                  "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
                  "requestedDeliveryDate":"2026-06-10"
                }
                """));
            }),
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await client2.PostAsync("/api/scheduling/orders", Json("""
                {
                  "caseName":"Concurrent Weekly B",
                  "impressionDate":"2026-06-08",
                  "productCategory":"temporary",
                  "material":"pmma",
                  "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
                  "requestedDeliveryDate":"2026-06-11"
                }
                """));
            })
        };

        var responses = await Task.WhenAll(tasks);
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Created), Is.EqualTo(1));
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest), Is.EqualTo(1));
        var rejected = responses.Single(r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.That(await rejected.Content.ReadAsStringAsync(), Does.Contain("Weekly capacity exceeded"));

        await using var ctx = OpenDb(fixture.DbPath);
        var activeCount = await ctx.SchedulingOrders.CountAsync(o => o.Status != nameof(OrderStatus.Cancelled) && o.RequestedDeliveryDate >= new DateOnly(2026, 6, 10) && o.RequestedDeliveryDate <= new DateOnly(2026, 6, 11));
        Assert.That(activeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SchedulingFlow_ConcurrentUpdatesCannotOverbookDailyCapacity()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 1.0m, 10.0m);
        using var setupClient = fixture.CreateClient();
        await LoginAsync(setupClient);
        var firstCode = await CreateOrderAsync(setupClient, "Concurrent Update A", "2026-06-11", material: "pmma", productCategory: "temporary", impressionDate: "2026-06-08");
        var secondCode = await CreateOrderAsync(setupClient, "Concurrent Update B", "2026-06-12", material: "pmma", productCategory: "temporary", impressionDate: "2026-06-08");

        using var client1 = fixture.CreateClient();
        using var client2 = fixture.CreateClient();
        await LoginAsync(client1);
        await LoginAsync(client2);
        using var barrier = new Barrier(2);

        var tasks = new[]
        {
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await client1.PutAsync($"/api/scheduling/orders/{firstCode}", Json("""
                {
                  "caseName":"Concurrent Update A",
                  "impressionDate":"2026-06-08",
                  "productCategory":"temporary",
                  "material":"pmma",
                  "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
                  "requestedDeliveryDate":"2026-06-10"
                }
                """));
            }),
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await client2.PutAsync($"/api/scheduling/orders/{secondCode}", Json("""
                {
                  "caseName":"Concurrent Update B",
                  "impressionDate":"2026-06-08",
                  "productCategory":"temporary",
                  "material":"pmma",
                  "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
                  "requestedDeliveryDate":"2026-06-10"
                }
                """));
            })
        };

        var responses = await Task.WhenAll(tasks);
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest), Is.EqualTo(1));
        var rejected = responses.Single(r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.That(await rejected.Content.ReadAsStringAsync(), Does.Contain("Daily capacity exceeded"));

        await using var ctx = OpenDb(fixture.DbPath);
        var activeOnTargetDate = await ctx.SchedulingOrders.CountAsync(o => o.Status != nameof(OrderStatus.Cancelled) && o.RequestedDeliveryDate == new DateOnly(2026, 6, 10));
        Assert.That(activeOnTargetDate, Is.EqualTo(1));
    }

    [Test]
    public async Task SchedulingFlow_CreatePmmaTelioOrder_RoundTripsMaterial()
    {
        using var fixture = NewSchedulingFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var create = await client.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Telio Temp",
          "impressionDate":"2026-06-02",
          "productCategory":"temporary",
          "material":"pmmaTelio",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-06-05"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var order = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        var code = order.GetProperty("orderCode").GetString();
        Assert.That(order.GetProperty("material").GetString(), Is.EqualTo("pmmaTelio"));

        var get = await client.GetAsync($"/api/scheduling/orders/{code}");
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var reloaded = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(reloaded.GetProperty("material").GetString(), Is.EqualTo("pmmaTelio"));
    }

    [Test]
    public async Task SchedulingFlow_UnauthenticatedOrderListReturns401()
    {
        using var fixture = NewSchedulingFixture();
        using var client = fixture.Client;

        var response = await client.GetAsync("/api/scheduling/orders");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SchedulingAuthResponses_UseOrganizationAndLabFields()
    {
        using var fixture = NewSchedulingFixture();

        using var clinic = fixture.Client;
        var clinicLogin = await clinic.PostAsync("/api/scheduling/auth/login", Json("{\"organizationCode\":\"DEMO\",\"pin\":\"123456\"}"));
        Assert.That(clinicLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var clinicJson = JsonDocument.Parse(await clinicLogin.Content.ReadAsStringAsync());
        Assert.That(clinicJson.RootElement.GetProperty("organizationType").GetString(), Is.EqualTo("clinic"));
        Assert.That(clinicJson.RootElement.GetProperty("isClinic").GetBoolean(), Is.True);

        using var lab = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(lab);
        var me = await lab.GetAsync("/api/scheduling/auth/me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var meJson = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.That(meJson.RootElement.GetProperty("organizationType").GetString(), Is.EqualTo("lab"));
        Assert.That(meJson.RootElement.GetProperty("isLab").GetBoolean(), Is.True);
        Assert.That(meJson.RootElement.GetProperty("organizationCode").GetString(), Is.EqualTo("LAB"));
    }

    [Test]
    public async Task SchedulingFlow_LabCanListAndCreateWithTargetClinic()
    {
        using var fixture = NewSchedulingFixture();
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
        await ApiTestFixture.LoginAsLabAsync(techClient);
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
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
    public async Task NonWorkingDaysEndpoint_ReturnsProviderDatesForRange()
    {
        using var fixture = NewSchedulingFixture();
        using var client = fixture.CreateClient();
        await LoginAsync(client);

        var response = await client.GetAsync("/api/scheduling/non-working-days?start=2026-05-01&end=2026-05-10");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var dates = doc.RootElement.GetProperty("dates").EnumerateArray().Select(x => x.GetString()).ToList();

        Assert.That(dates, Does.Contain("2026-05-01"));
        Assert.That(dates, Does.Contain("2026-05-06"));
        Assert.That(dates, Does.Not.Contain("2026-05-05"));
    }

    [Test]
    public async Task NonWorkingDaysEndpoint_RequiresAuthAndValidatesRange()
    {
        using var fixture = NewSchedulingFixture();
        using var anonymous = fixture.CreateClient();

        var unauthenticated = await anonymous.GetAsync("/api/scheduling/non-working-days?start=2026-06-01&end=2026-06-30");
        Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var client = fixture.CreateClient();
        await LoginAsync(client);

        var invalid = await client.GetAsync("/api/scheduling/non-working-days?start=2026-06-30&end=2026-06-01");
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SchedulingCalendarEndpoint_IsRoleAwareInclusiveAndExcludesCancelled()
    {
        using var fixture = NewSchedulingFixture();
        using var clinicClient = fixture.Client;
        await LoginAsync(clinicClient);

        var demoStart = await CreateOrderAsync(clinicClient, "Demo Start", "2026-06-05");
        var demoEndCancelled = await CreateOrderAsync(clinicClient, "Demo Cancelled", "2026-06-10");
        var demoOutside = await CreateOrderAsync(clinicClient, "Demo Outside", "2026-06-12");
        var cancel = await clinicClient.DeleteAsync($"/api/scheduling/orders/{demoEndCancelled}");
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var techClient = fixture.CreateClient();
        await ApiTestFixture.LoginAsLabAsync(techClient);
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
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
    public async Task SchedulingDates_GivenEditOrderCode_UsesExistingOrderCreatedAtForPreview()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 5, 9, 30, 0, TimeSpan.Zero));
        const string code = "EDT-001";
        await SeedOrderAsync(
            fixture.DbPath,
            code,
            "Edit preview",
            "DEMO",
            "2026-06-05",
            createdAt: DateTimeOffset.Parse("2026-06-02T07:30:00Z"));
        using var client = fixture.Client;
        await LoginAsync(client);

        var withoutEditContext = await client.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Edit preview",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "start":"2026-06-01",
          "end":"2026-06-12"
        }
        """));
        var withEditContext = await client.PostAsync("/api/scheduling/dates", Json($$"""
        {
          "orderCode":"{{code}}",
          "caseName":"Edit preview",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "start":"2026-06-01",
          "end":"2026-06-12"
        }
        """));

        Assert.That(withoutEditContext.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(withEditContext.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var withoutDoc = JsonDocument.Parse(await withoutEditContext.Content.ReadAsStringAsync());
        var withDoc = JsonDocument.Parse(await withEditContext.Content.ReadAsStringAsync());
        Assert.That(withoutDoc.RootElement.GetProperty("minimumDate").GetString(), Is.EqualTo("2026-06-11"));
        Assert.That(withDoc.RootElement.GetProperty("minimumDate").GetString(), Is.EqualTo("2026-06-05"));
        var statuses = withDoc.RootElement.GetProperty("dates").EnumerateArray().ToDictionary(e => e.GetProperty("date").GetString()!);
        Assert.That(statuses["2026-06-05"].GetProperty("isSelectable").GetBoolean(), Is.True);
    }

    [Test]
    public async Task SchedulingDates_GivenMultipleOrderWorkItems_UsesMaterialLeadTime()
    {
        using var fixture = NewSchedulingFixture();
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
        Assert.That(doc.RootElement.GetProperty("minimumDate").GetString(), Is.EqualTo("2026-06-05"));
    }

    [Test]
    public async Task SchedulingFlow_RetiredLegacyTechnicianOrdersRouteReturns404()
    {
        using var fixture = NewSchedulingFixture();
        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);

        var response = await client.GetAsync("/api/scheduling/technician/orders");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SchedulingFlow_RejectsInvalidPinAndUnauthenticatedAccess()
    {
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
        using var fixture = NewSchedulingFixture();
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
        await ApiTestFixture.LoginAsLabAsync(techClient);
        var techFind = await techClient.GetAsync("/api/scheduling/orders/find?code=27-0605-Z1AA");
        Assert.That(techFind.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var ambiguousShort = await techClient.GetAsync("/api/scheduling/orders/find?code=0605-Z1AA");
        Assert.That(ambiguousShort.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task DeadlineRecommendationLogsEndpoint_LabCanInspectCreateAndUpdateLogsAndClinicForbidden()
    {
        using var fixture = NewSchedulingFixture();
        using var clinicClient = fixture.Client;
        await LoginAsync(clinicClient);
        var code = await CreateOrderAsync(clinicClient, "Logged API", "2026-06-05");

        var update = await clinicClient.PutAsync($"/api/scheduling/orders/{code}", Json("""
        {
          "caseName":"Logged API Updated",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-06-09"
        }
        """));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var forbidden = await clinicClient.GetAsync($"/api/scheduling/orders/{code}/deadline-recommendation-logs");
        Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var anon = fixture.CreateClient();
        var unauth = await anon.GetAsync($"/api/scheduling/orders/{code}/deadline-recommendation-logs");
        Assert.That(unauth.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var labClient = fixture.CreateClient();
        await ApiTestFixture.LoginAsLabAsync(labClient);
        var logs = await labClient.GetAsync($"/api/scheduling/orders/{code}/deadline-recommendation-logs");

        Assert.That(logs.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await logs.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.That(items, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(items[0].GetProperty("orderCode").GetString(), Is.EqualTo(code));
            Assert.That(items[0].GetProperty("selectedDeadlineDate").GetString(), Is.EqualTo("2026-06-09"));
            Assert.That(items[0].GetProperty("finalRecommendedDeadlineDate").GetString(), Is.EqualTo("2026-06-05"));
            Assert.That(items[0].GetProperty("calculatedOrderCapacityUnits").GetDecimal(), Is.EqualTo(1.0m));
            Assert.That(items[0].GetProperty("candidateChecksJson").GetString(), Does.Contain("candidateDate"));
            Assert.That(items[0].GetProperty("configSnapshotJson").GetString(), Does.Contain("materialConfig"));
        });
    }

    [Test]
    public async Task DeadlineOverride_ClinicCannotOverrideButLabCanAndLogsAreRetrievable()
    {
        using var fixture = NewSchedulingFixture(new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        await UpsertCapacityConfigAsync(fixture.DbPath, new DateOnly(2026, 1, 1), 1.0m, 10.0m);
        using var clinicClient = fixture.Client;
        await LoginAsync(clinicClient);
        var firstCode = await CreateOrderAsync(clinicClient, "Capacity First", "2026-06-10", material: "pmma", productCategory: "temporary", impressionDate: "2026-06-08");

        var clinicOverride = await clinicClient.PostAsync("/api/scheduling/orders", Json("""
        {
          "caseName":"Clinic Override Attempt",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "requestedDeliveryDate":"2026-06-10",
          "confirmDeadlineOverride":true,
          "deadlineOverrideReason":"please overbook"
        }
        """));
        Assert.That(clinicOverride.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var clinicError = JsonDocument.Parse(await clinicOverride.Content.ReadAsStringAsync()).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(clinicError.GetProperty("overrideAllowed").GetBoolean(), Is.False);
            Assert.That(clinicError.GetProperty("failedRules").EnumerateArray().Select(e => e.GetString()), Does.Contain("DailyCapacityExceeded"));
        });

        using var labClient = fixture.CreateClient();
        await ApiTestFixture.LoginAsLabAsync(labClient);
        var noReason = await labClient.PostAsync("/api/scheduling/orders", Json("""
        {
          "clinicCode":"OTHER",
          "caseName":"Lab No Reason",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "requestedDeliveryDate":"2026-06-10",
          "confirmDeadlineOverride":true,
          "deadlineOverrideReason":" "
        }
        """));
        Assert.That(noReason.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await noReason.Content.ReadAsStringAsync(), Does.Contain("reason"));

        var labOverride = await labClient.PostAsync("/api/scheduling/orders", Json("""
        {
          "clinicCode":"OTHER",
          "caseName":"Lab Override",
          "impressionDate":"2026-06-08",
          "productCategory":"temporary",
          "material":"pmma",
          "workItems":[{"constructionType":"crown","toothStart":12,"toothEnd":12}],
          "requestedDeliveryDate":"2026-06-10",
          "confirmDeadlineOverride":true,
          "deadlineOverrideReason":"Doctor requested rush remake"
        }
        """));
        Assert.That(labOverride.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var overrideOrder = JsonDocument.Parse(await labOverride.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        var overrideCode = overrideOrder.GetProperty("orderCode").GetString()!;
        Assert.That(overrideOrder.GetProperty("requestedDeliveryDate").GetString(), Is.EqualTo("2026-06-10"));

        var clinicLogAccess = await clinicClient.GetAsync($"/api/scheduling/orders/{overrideCode}/deadline-override-logs");
        Assert.That(clinicLogAccess.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        using var anon = fixture.CreateClient();
        var anonLogAccess = await anon.GetAsync($"/api/scheduling/orders/{overrideCode}/deadline-override-logs");
        Assert.That(anonLogAccess.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var overrideLogs = await labClient.GetAsync($"/api/scheduling/orders/{overrideCode}/deadline-override-logs");
        Assert.That(overrideLogs.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var log = JsonDocument.Parse(await overrideLogs.Content.ReadAsStringAsync()).RootElement.GetProperty("items")[0];
        Assert.Multiple(() =>
        {
            Assert.That(log.GetProperty("overrideReason").GetString(), Is.EqualTo("Doctor requested rush remake"));
            Assert.That(log.GetProperty("rulesBypassedJson").GetString(), Does.Contain("DailyCapacityExceeded"));
            Assert.That(log.GetProperty("existingDailyCapacityUsed").GetDecimal(), Is.EqualTo(1.0m));
            Assert.That(log.GetProperty("dailyCapacityAfterOverride").GetDecimal(), Is.EqualTo(2.0m));
            Assert.That(log.GetProperty("recommendationLogId").GetInt64(), Is.GreaterThan(0));
        });

        var recommendationLogs = await labClient.GetAsync($"/api/scheduling/orders/{overrideCode}/deadline-recommendation-logs");
        Assert.That(recommendationLogs.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(JsonDocument.Parse(await recommendationLogs.Content.ReadAsStringAsync()).RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(1));

        await using var ctx = OpenDb(fixture.DbPath);
        Assert.That(await ctx.SchedulingDeadlineOverrideLogs.CountAsync(), Is.EqualTo(1));
        Assert.That(await ctx.SchedulingDeadlineRecommendationLogs.CountAsync(l => l.OrderCode == overrideCode), Is.EqualTo(1));
        Assert.That(await ctx.SchedulingOrders.CountAsync(o => o.OrderCode == firstCode || o.OrderCode == overrideCode), Is.EqualTo(2));
    }

    [Test]
    public async Task DeadlineOverride_LabCanOverrideCalendarBlockedDate()
    {
        using var fixture = NewSchedulingFixture();
        using var labClient = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(labClient);

        var create = await labClient.PostAsync("/api/scheduling/orders", Json("""
        {
          "clinicCode":"OTHER",
          "caseName":"Calendar Override",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"2026-06-01",
          "confirmDeadlineOverride":true,
          "deadlineOverrideReason":"Special hand delivery arranged"
        }
        """));

        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var code = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("order").GetProperty("orderCode").GetString()!;
        var logs = await labClient.GetAsync($"/api/scheduling/orders/{code}/deadline-override-logs");
        var log = JsonDocument.Parse(await logs.Content.ReadAsStringAsync()).RootElement.GetProperty("items")[0];
        Assert.That(log.GetProperty("rulesBypassedJson").GetString(), Does.Contain("CalendarDeadlineBlocked"));
    }

    [Test]
    public async Task SchedulingDatesPreview_DoesNotCreateDeadlineRecommendationLog()
    {
        using var fixture = NewSchedulingFixture();
        using var clinicClient = fixture.Client;
        await LoginAsync(clinicClient);

        var dates = await clinicClient.PostAsync("/api/scheduling/dates", Json("""
        {
          "caseName":"Preview Only",
          "impressionDate":"2026-06-02",
          "productCategory":"permanent",
          "material":"fullContourZirconia",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "start":"2026-06-01",
          "end":"2026-06-10"
        }
        """));
        Assert.That(dates.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await using var ctx = OpenDb(fixture.DbPath);
        Assert.That(await ctx.SchedulingDeadlineRecommendationLogs.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.SchedulingDeadlineOverrideLogs.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task SchedulingConfigReloadEndpoint_IsRemoved()
    {
        using var fixture = NewSchedulingFixture();
        using var client = fixture.Client;
        await LoginAsync(client);

        var response = await client.PostAsync("/api/scheduling/config/reload", Json("{}"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SchedulingClinicsEndpoint_ReturnsDisplayColorAndLinkedClientForLab()
    {
        using var fixture = NewSchedulingFixture();
        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);

        var response = await client.GetAsync("/api/scheduling/clinics");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var demo = doc.RootElement.GetProperty("items").EnumerateArray()
            .First(e => e.GetProperty("clinicCode").GetString() == "DEMO");
        Assert.That(demo.GetProperty("clinicDisplayName").GetString(), Is.EqualTo("Demo Dental Clinic"));
        Assert.That(demo.GetProperty("clinicDisplayColor").GetString(), Is.EqualTo("#7c3aed"));
        Assert.That(demo.GetProperty("linkedClientNickname").GetString(), Is.EqualTo("demo-client"));
    }

    [Test]
    public async Task SchedulingOrdersList_IncludesClinicMetadataForLab()
    {
        using var fixture = NewSchedulingFixture();
        await SeedOrderAsync(fixture.DbPath, "META-001", "Meta Demo", "DEMO", "2026-06-05");
        await SeedOrderAsync(fixture.DbPath, "META-002", "Meta Other", "OTHER", "2026-06-06");

        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);
        var response = await client.GetAsync("/api/scheduling/orders?limit=50");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.TryGetProperty("clinics", out var clinics), Is.True);
        Assert.That(clinics.TryGetProperty("DEMO", out var demo), Is.True);
        Assert.That(demo.GetProperty("clinicDisplayColor").GetString(), Is.EqualTo("#7c3aed"));
        Assert.That(demo.GetProperty("linkedClientNickname").GetString(), Is.EqualTo("demo-client"));
        Assert.That(clinics.TryGetProperty("OTHER", out var other), Is.True);
        Assert.That(other.GetProperty("clinicDisplayColor").GetString(), Is.EqualTo("#0ea5e9"));
    }

    [Test]
    public async Task SchedulingOrdersList_ClinicScopedDoesNotIncludeClinicsMap()
    {
        using var fixture = NewSchedulingFixture();
        await SeedOrderAsync(fixture.DbPath, "CLN-001", "Clinic Only", "DEMO", "2026-06-05");

        using var client = fixture.Client;
        await LoginAsync(client);
        var response = await client.GetAsync("/api/scheduling/orders?limit=50");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.TryGetProperty("clinics", out _), Is.False);
    }

    [Test]
    public async Task SchedulingCalendar_IncludesClinicMetadataForLab()
    {
        using var fixture = NewSchedulingFixture();
        await SeedOrderAsync(fixture.DbPath, "CAL-001", "Calendar Demo", "DEMO", "2026-06-05");

        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);
        var response = await client.GetAsync("/api/scheduling/orders/calendar?start=2026-06-01&end=2026-06-30");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.TryGetProperty("clinics", out var clinics), Is.True);
        Assert.That(clinics.TryGetProperty("DEMO", out var demo), Is.True);
        Assert.That(demo.GetProperty("clinicDisplayColor").GetString(), Is.EqualTo("#7c3aed"));
    }

    [Test]
    public async Task SchedulingOrderDetail_IncludesClinicMetadataForLab()
    {
        using var fixture = NewSchedulingFixture();
        const string code = "DET-001";
        await SeedOrderAsync(fixture.DbPath, code, "Detail Demo", "DEMO", "2026-06-05");

        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);
        var response = await client.GetAsync($"/api/scheduling/orders/{code}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var order = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(order.GetProperty("clinicDisplayColor").GetString(), Is.EqualTo("#7c3aed"));
        Assert.That(order.GetProperty("linkedClientNickname").GetString(), Is.EqualTo("demo-client"));
    }

    [Test]
    public async Task SchedulingOrdersFind_ListPageIncludesClinicMetadataForLab()
    {
        using var fixture = NewSchedulingFixture();
        const string code = "FND-001";
        await SeedOrderAsync(fixture.DbPath, code, "Find Demo", "DEMO", "2026-06-05");

        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);
        var response = await client.GetAsync($"/api/scheduling/orders/find?code={code}&limit=50");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.GetProperty("order").GetProperty("clinicDisplayColor").GetString(), Is.EqualTo("#7c3aed"));
        Assert.That(doc.RootElement.GetProperty("listPage").TryGetProperty("clinics", out var clinics), Is.True);
        Assert.That(clinics.TryGetProperty("DEMO", out _), Is.True);
    }

    [Test]
    public async Task SchedulingClinicMetadata_InactiveClinicHistoricalOrderOmitsLiveMetadata()
    {
        using var fixture = NewSchedulingFixture();
        _ = fixture.CreateClient();
        const string code = "INA-001";
        await SeedOrderAsync(fixture.DbPath, code, "Inactive Clinic Order", "OTHER", "2026-06-05");
        await DeactivateClinicAsync(fixture.DbPath, "OTHER");

        using var client = fixture.Client;
        await ApiTestFixture.LoginAsLabAsync(client);
        var response = await client.GetAsync("/api/scheduling/orders?limit=50");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.GetProperty("items").EnumerateArray().Any(e => e.GetProperty("orderCode").GetString() == code), Is.True);
        if (doc.RootElement.TryGetProperty("clinics", out var clinics))
            Assert.That(clinics.TryGetProperty("OTHER", out _), Is.False);

        var detail = await client.GetAsync($"/api/scheduling/orders/{code}");
        Assert.That(detail.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var order = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()).RootElement.GetProperty("order");
        Assert.That(order.GetProperty("clinicDisplayName").GetString(), Is.EqualTo("Other Clinic"));
        Assert.That(order.TryGetProperty("clinicDisplayColor", out _), Is.False);
        Assert.That(order.TryGetProperty("linkedClientNickname", out _), Is.False);
    }

    private static async Task DeactivateClinicAsync(string dbPath, string clinicCode)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using var ctx = new AppDbContext(options);
        var clinic = await ctx.SchedulingClinics.SingleAsync(c => c.Code == clinicCode);
        clinic.IsActive = false;
        clinic.UpdatedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();
    }

    private static async Task UpdateMaterialConfigAsync(string dbPath, Material material, int? fixedLeadTimeBusinessDays = null, int? teethPerExtraLeadDay = null, decimal? capacityUnitsPerTooth = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using var ctx = new AppDbContext(options);
        await ctx.Database.MigrateAsync();
        var row = await ctx.SchedulingMaterialConfigs
            .Where(c => c.Material == material)
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .FirstAsync();
        if (fixedLeadTimeBusinessDays.HasValue) row.FixedLeadTimeBusinessDays = fixedLeadTimeBusinessDays.Value;
        if (teethPerExtraLeadDay.HasValue) row.TeethPerExtraLeadDay = teethPerExtraLeadDay.Value;
        if (capacityUnitsPerTooth.HasValue) row.CapacityUnitsPerTooth = capacityUnitsPerTooth.Value;
        row.UpdatedAt = DateTimeOffset.Parse("2026-06-21T00:00:00Z");
        await ctx.SaveChangesAsync();
    }

    private static async Task RemoveMaterialConfigRowsAsync(string dbPath, Material material)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using var ctx = new AppDbContext(options);
        await ctx.Database.MigrateAsync();
        var rows = await ctx.SchedulingMaterialConfigs.Where(c => c.Material == material).ToListAsync();
        ctx.SchedulingMaterialConfigs.RemoveRange(rows);
        await ctx.SaveChangesAsync();
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var login = await client.PostAsync("/api/scheduling/auth/login", Json("{\"clinicCode\":\"DEMO\",\"pin\":\"123456\"}"));
        var cookie = login.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("s3d_order_session="));
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
    }

    private static async Task<string> CreateOrderAsync(
        HttpClient client,
        string caseName,
        string requestedDeliveryDate,
        string? clinicCode = null,
        string material = "fullContourZirconia",
        string productCategory = "permanent",
        string impressionDate = "2026-06-02")
    {
        var clinicPrefix = clinicCode == null ? "" : $"\"clinicCode\":\"{clinicCode}\",";
        var create = await client.PostAsync("/api/scheduling/orders", Json($$"""
        {
          {{clinicPrefix}}
          "caseName":"{{caseName}}",
          "impressionDate":"{{impressionDate}}",
          "productCategory":"{{productCategory}}",
          "material":"{{material}}",
          "workItems":[{"constructionType":"crown","toothStart":11,"toothEnd":11}],
          "requestedDeliveryDate":"{{requestedDeliveryDate}}"
        }
        """));
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        return JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement.GetProperty("order").GetProperty("orderCode").GetString()!;
    }

    private static async Task<string> SeedOrderAsync(string dbPath, string code, string caseName, string clinicCode, string requestedDeliveryDate, DateTimeOffset? createdAt = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using (var ctx = new AppDbContext(options))
            await ctx.Database.MigrateAsync();
        var repo = new SqliteOrderRepo(() => new AppDbContext(options));
        var timestamp = createdAt ?? DateTimeOffset.Parse("2026-06-02T12:00:00Z");
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
            timestamp,
            timestamp,
            "127.0.0.1",
            "test",
            null,
            1.0m));
        return code;
    }

    private static AppDbContext OpenDb(string dbPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task UpsertCapacityConfigAsync(string dbPath, DateOnly activeFromDate, decimal dailyCapacityUnits, decimal weeklyCapacityUnits)
    {
        await using var ctx = OpenDb(dbPath);
        await ctx.Database.MigrateAsync();
        var row = await ctx.SchedulingCapacityConfigs.SingleOrDefaultAsync(c => c.ActiveFromDate == activeFromDate);
        if (row == null)
        {
            ctx.SchedulingCapacityConfigs.Add(new SchedulingCapacityConfigEntity
            {
                ActiveFromDate = activeFromDate,
                DailyCapacityUnits = dailyCapacityUnits,
                WeeklyCapacityUnits = weeklyCapacityUnits,
                CreatedAt = DateTimeOffset.Parse("2026-06-21T00:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-06-21T00:00:00Z")
            });
        }
        else
        {
            row.DailyCapacityUnits = dailyCapacityUnits;
            row.WeeklyCapacityUnits = weeklyCapacityUnits;
            row.UpdatedAt = DateTimeOffset.Parse("2026-06-21T00:00:00Z");
        }

        await ctx.SaveChangesAsync();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
