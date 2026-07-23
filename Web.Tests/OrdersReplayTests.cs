using System.Net;
using Database;
using Database.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Orders;
using PuppeteerSharp;

namespace Web.Tests;

public class OrdersReplayTests
{
    [Test]
    [Category("OrdersReplay")]
    public async Task OrdersCoreReplay_LoginRoutesCreateConfirmationAndDirtyGuard_Work()
    {
        using var fixture = new ApiTestFixture();
        _ = fixture.Client;
        const string seedCode = "26-0605-Z1AA";
        await SeedOrderAsync(fixture.DbPath, seedCode, "Replay seeded review", "DEMO", "2026-06-05");

        await using var server = await BrowserBridgeServer.StartAsync(fixture);
        await using var browser = await LaunchBrowserAsync();
        await using var page = await browser.NewPageAsync();
        page.DefaultTimeout = 10_000;
        page.DefaultNavigationTimeout = 15_000;

        await page.GoToAsync(server.Url("/orders"));
        await WaitForFunctionAsync(page, "location.pathname === '/login' && new URLSearchParams(location.search).get('returnUrl') === '/orders'");
        await page.TypeAsync("#organizationCode", "DEMO");
        await page.TypeAsync("#pin", "123456");
        await page.ClickAsync("#loginBtn");
        await WaitForFunctionAsync(page, "location.pathname === '/orders'");
        await WaitVisibleAsync(page, "#list");
        Assert.That(await HashAsync(page), Is.EqualTo(string.Empty));

        await page.ClickAsync("#ordersCalendarModeBtn");
        await WaitForVisibleStateAsync(page, "#ordersCalendarWrap", visible: true);
        Assert.That(await HashAsync(page), Is.EqualTo(string.Empty));
        await page.ClickAsync("#ordersListModeBtn");
        await WaitForVisibleStateAsync(page, "#ordersListWrap", visible: true);
        Assert.That(await HashAsync(page), Is.EqualTo(string.Empty));

        await page.ClickAsync("#openFindOrderBtn");
        await WaitVisibleAsync(page, "#findOrderPopup");
        await WaitForFunctionAsync(page, "document.activeElement && document.activeElement.id === 'orderFindInput'");
        await page.Keyboard.PressAsync("Escape");
        await WaitForHiddenAsync(page, "#findOrderPopup");

        await page.GoToAsync(server.Url($"/orders#order/{Uri.EscapeDataString(seedCode)}"));
        await WaitVisibleAsync(page, "#reviewCard");
        await WaitForTextAsync(page, "#reviewCaseName", "Replay seeded review");
        Assert.That(await page.EvaluateExpressionAsync<string>("document.querySelector('#reviewCode').textContent"), Does.Contain("0605-Z1AA"));
        await page.ClickAsync("#reviewCancelBtn");
        await WaitVisibleAsync(page, "#cancelOrderConfirmPopup");
        await page.Keyboard.PressAsync("Escape");
        await WaitForHiddenAsync(page, "#cancelOrderConfirmPopup");
        await page.EvaluateExpressionAsync("document.querySelector('#reviewBackTopBtn').click()");
        await WaitForHashAsync(page, "");
        await WaitVisibleAsync(page, "#list");
        await WaitForHiddenAsync(page, "#reviewCard");
        Assert.That(await HashAsync(page), Is.EqualTo(string.Empty));

        await page.ClickAsync("#newOrderBtn");
        await WaitVisibleAsync(page, "#app");
        await WaitForHashAsync(page, "#new/1");

        await page.EvaluateExpressionAsync("document.querySelector(\"#quickTeeth .tooth[data-t='11']\").click()");
        await WaitForFunctionAsync(page, "document.querySelector('#ts') && document.querySelector('#ts').value === '11'");
        await page.ClickAsync("#cancelCreateBtn");
        await WaitVisibleAsync(page, "#discardOrderFlowPopup");
        await page.ClickAsync("#discardOrderFlowBackBtn");
        await WaitForHiddenAsync(page, "#discardOrderFlowPopup");
        await WaitForHashAsync(page, "#new/1");
        await page.ClickAsync("#cancelCreateBtn");
        await WaitVisibleAsync(page, "#discardOrderFlowPopup");
        await page.ClickAsync("#discardOrderFlowYesBtn");
        await WaitVisibleAsync(page, "#list");
        Assert.That(await HashAsync(page), Is.EqualTo(string.Empty));

        await page.ClickAsync("#newOrderBtn");
        await WaitForHashAsync(page, "#new/1");
        await page.EvaluateExpressionAsync("document.querySelector(\"#quickTeeth .tooth[data-t='11']\").click()");
        await WaitForFunctionAsync(page, "document.querySelector('#ts') && document.querySelector('#ts').value === '11'");
        await page.ClickAsync(".nav-next");
        await WaitForHashAsync(page, "#new/2");
        await page.ClickAsync("#materialChoices [data-mat='fullContourZirconia']");
        await page.ClickAsync("#shadeChoices [data-shade='A1']");
        await page.ClickAsync(".nav-next");
        await WaitForHashAsync(page, "#new/3");
        await page.WaitForSelectorAsync(".delivery-calendar-date:not([disabled])");
        await page.EvaluateExpressionAsync("document.querySelector('.delivery-calendar-date:not([disabled])').click()");
        await WaitForFunctionAsync(page, "document.querySelector('#selectedDeliveryDate') && !document.querySelector('#selectedDeliveryDate').classList.contains('date-display-placeholder')");
        await page.ClickAsync(".nav-next");
        await WaitForHashAsync(page, "#new/4");
        await page.TypeAsync("#caseName", "Replay Created Case");
        await page.ClickAsync(".nav-next");
        await WaitForFunctionAsync(page, "location.hash.startsWith('#created/')");
        await WaitVisibleAsync(page, "#app");
        await WaitForTextNotAsync(page, "#code", "—");
        var createdHash = await HashAsync(page);
        Assert.That(createdHash, Does.StartWith("#created/"));

        await page.ReloadAsync();
        await WaitForHashAsync(page, createdHash);
        await WaitVisibleAsync(page, "#app");
        await WaitForTextNotAsync(page, "#code", "—");
        await page.ClickAsync("#cancelCreateBtn");
        await WaitVisibleAsync(page, "#list");
        Assert.That(await HashAsync(page), Is.EqualTo(string.Empty));

        await page.ClickAsync("#btnAppMenu");
        await page.ClickAsync("#appChromeLogoutBtn");
        await WaitForFunctionAsync(page, "location.pathname === '/login'");
    }

    [Test]
    [Category("OrdersReplay")]
    public async Task OrdersReplay_DirtyGuard_PersistsAcrossStepNavigation()
    {
        using var fixture = new ApiTestFixture();
        _ = fixture.Client;
        const string seedCode = "26-0605-Z1AA";
        await SeedOrderAsync(fixture.DbPath, seedCode, "Replay seeded review", "DEMO", "2026-06-05");

        await using var server = await BrowserBridgeServer.StartAsync(fixture);
        await using var browser = await LaunchBrowserAsync();
        await using var page = await browser.NewPageAsync();
        page.DefaultTimeout = 10_000;
        page.DefaultNavigationTimeout = 15_000;

        await page.GoToAsync(server.Url("/orders"));
        await WaitForFunctionAsync(page, "location.pathname === '/login'");
        await page.TypeAsync("#organizationCode", "DEMO");
        await page.TypeAsync("#pin", "123456");
        await page.ClickAsync("#loginBtn");
        await WaitForFunctionAsync(page, "location.pathname === '/orders'");

        await page.ClickAsync("#newOrderBtn");
        await WaitForHashAsync(page, "#new/1");
        await WaitForVisibleStateAsync(page, "#app", visible: true);
        await page.EvaluateExpressionAsync("document.querySelector(\"#quickTeeth .tooth[data-t='11']\").click()");
        await WaitForFunctionAsync(page, "document.querySelector('#ts') && document.querySelector('#ts').value === '11'");
        await page.ClickAsync(".nav-next");
        await WaitForHashAsync(page, "#new/2");
        await WaitForVisibleStateAsync(page, "#app", visible: true);
        await page.ClickAsync("#cancelCreateBtn");
        await WaitVisibleAsync(page, "#discardOrderFlowPopup");
        await page.ClickAsync("#discardOrderFlowYesBtn");
        await WaitVisibleAsync(page, "#list");

        await page.GoToAsync(server.Url($"/orders#order/{Uri.EscapeDataString(seedCode)}"));
        await WaitVisibleAsync(page, "#reviewCard");
        await page.EvaluateExpressionAsync("document.querySelector('#reviewEditBtn').click()");
        await WaitForHashAsync(page, $"#edit/{Uri.EscapeDataString(seedCode)}/1");
        await WaitForVisibleStateAsync(page, "#app", visible: true);
        await page.EvaluateExpressionAsync("document.querySelector(\"#quickTeeth .tooth[data-t='12']\").click()");
        await WaitForFunctionAsync(page, "document.querySelector('#ts') && document.querySelector('#ts').value === '12'");
        await page.ClickAsync(".nav-next");
        await WaitForHashAsync(page, $"#edit/{Uri.EscapeDataString(seedCode)}/2");
        await WaitForVisibleStateAsync(page, "#app", visible: true);
        await page.ClickAsync("#cancelCreateBtn");
        await WaitVisibleAsync(page, "#discardOrderFlowPopup");
    }

    [Test]
    [Category("OrdersReplay")]
    public async Task OrdersReplay_HeaderExit_ReopensEditFlowWithFreshLoad()
    {
        using var fixture = new ApiTestFixture();
        _ = fixture.Client;
        const string seedCode = "26-0605-Z1AA";
        await SeedOrderAsync(fixture.DbPath, seedCode, "Replay seeded review", "DEMO", "2026-06-05");

        await using var server = await BrowserBridgeServer.StartAsync(fixture);
        await using var browser = await LaunchBrowserAsync();
        await using var page = await browser.NewPageAsync();
        page.DefaultTimeout = 10_000;
        page.DefaultNavigationTimeout = 15_000;

        await page.GoToAsync(server.Url("/orders"));
        await WaitForFunctionAsync(page, "location.pathname === '/login'");
        await page.TypeAsync("#organizationCode", "DEMO");
        await page.TypeAsync("#pin", "123456");
        await page.ClickAsync("#loginBtn");
        await WaitForFunctionAsync(page, "location.pathname === '/orders'");

        await page.GoToAsync(server.Url($"/orders#order/{Uri.EscapeDataString(seedCode)}"));
        await WaitVisibleAsync(page, "#reviewCard");
        await page.EvaluateExpressionAsync("document.querySelector('#reviewEditBtn').click()");
        await WaitForHashAsync(page, $"#edit/{Uri.EscapeDataString(seedCode)}/1");
        await WaitForVisibleStateAsync(page, "#app", visible: true);
        await page.ClickAsync(".nav-next");
        await WaitForHashAsync(page, $"#edit/{Uri.EscapeDataString(seedCode)}/2");
        await WaitForVisibleStateAsync(page, "#app", visible: true);

        await page.ClickAsync("#appChromeBrand");
        await WaitForHashAsync(page, "");
        await WaitVisibleAsync(page, "#list");
        await WaitForHiddenAsync(page, "#app");

        await page.GoToAsync(server.Url($"/orders#order/{Uri.EscapeDataString(seedCode)}"));
        await WaitVisibleAsync(page, "#reviewCard");
        await page.EvaluateExpressionAsync("document.querySelector('#reviewEditBtn').click()");
        await WaitForHashAsync(page, $"#edit/{Uri.EscapeDataString(seedCode)}/1");
        await WaitForVisibleStateAsync(page, "#app", visible: true);
        await WaitForFunctionAsync(page, "document.querySelector('.step[data-s=\"1\"]') && document.querySelector('.step[data-s=\"1\"]').classList.contains('active')");
    }

    [Test]
    [Category("OrdersReplay")]
    public async Task OrdersPage_BfcacheRestoreAfterLogout_DoesNotRevealCachedOrdersShell()
    {
        using var fixture = new ApiTestFixture();
        _ = fixture.Client;

        await using var server = await BrowserBridgeServer.StartAsync(fixture);
        await using var browser = await LaunchBrowserAsync();
        await using var page = await browser.NewPageAsync();
        page.DefaultTimeout = 10_000;
        page.DefaultNavigationTimeout = 15_000;

        await page.GoToAsync(server.Url("/orders"));
        await WaitForFunctionAsync(page, "location.pathname === '/login'");
        await page.TypeAsync("#organizationCode", "DEMO");
        await page.TypeAsync("#pin", "123456");
        await page.ClickAsync("#loginBtn");
        await WaitForFunctionAsync(page, "location.pathname === '/orders'");
        await WaitVisibleAsync(page, "#list");

        var logoutOk = await page.EvaluateExpressionAsync<bool>("fetch('/api/scheduling/auth/logout',{method:'POST',headers:{'content-type':'application/json'},body:'{}'}).then(r=>r.ok)");
        Assert.That(logoutOk, Is.True);
        await WaitForFunctionAsync(page, "document.querySelector('#list') && !document.querySelector('#list').classList.contains('hidden')");

        await page.EvaluateExpressionAsync("window.dispatchEvent(new PageTransitionEvent('pageshow',{persisted:true}))");

        await WaitForLoginWithoutOrdersShellOrFailAsync(page);
    }

    private static async Task<IBrowser> LaunchBrowserAsync()
    {
        try
        {
            var executablePath = FindBundledChromiumExecutable();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                var fetcher = new BrowserFetcher();
                var installed = await fetcher.DownloadAsync();
                executablePath = installed.GetExecutablePath();
            }

            return await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = executablePath,
                Args = new[] { "--no-sandbox", "--disable-dev-shm-usage" }
            });
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Chromium is unavailable for Orders replay tests: {ex.Message}");
            throw;
        }
    }

    private static string? FindBundledChromiumExecutable()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var candidates = new[]
        {
            Path.Combine(testDir, "chromium"),
            Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "Web", "bin", "Debug", "net9.0", "chromium"))
        };
        var fileName = OperatingSystem.IsWindows() ? "chrome.exe" : "chrome";
        return candidates
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories))
            .FirstOrDefault();
    }

    private static async Task WaitVisibleAsync(IPage page, string selector) =>
        await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Visible = true });

    private static async Task WaitForHiddenAsync(IPage page, string selector) =>
        await page.WaitForFunctionAsync("sel => !document.querySelector(sel) || document.querySelector(sel).classList.contains('hidden')", args: new object[] { selector });

    private static async Task WaitForVisibleStateAsync(IPage page, string selector, bool visible) =>
        await page.WaitForFunctionAsync(
            "(sel, visible) => { const el = document.querySelector(sel); return !!el && (el.classList.contains('hidden') !== visible); }",
            args: new object[] { selector, visible });

    private static async Task WaitForHashAsync(IPage page, string hash) =>
        await page.WaitForFunctionAsync("expected => location.hash === expected", args: new object[] { hash });

    private static async Task WaitForFunctionAsync(IPage page, string expression) =>
        await page.WaitForFunctionAsync($"() => ({expression})");

    private static async Task WaitForLoginWithoutOrdersShellOrFailAsync(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "() => location.pathname === '/login' && !document.querySelector('#list')",
                new WaitForFunctionOptions { Timeout = 3_000 });
        }
        catch (WaitTaskTimeoutException)
        {
            var location = await page.EvaluateExpressionAsync<string>("location.pathname + location.search + location.hash");
            var listVisible = await page.EvaluateExpressionAsync<bool>("!!document.querySelector('#list') && !document.querySelector('#list').classList.contains('hidden')");
            Assert.Fail($"Expected bfcache restore after logout to redirect to /login and remove the cached orders shell, but browser stayed at '{location}' with orders list visible: {listVisible}.");
        }
    }

    private static async Task WaitForTextAsync(IPage page, string selector, string text) =>
        await page.WaitForFunctionAsync("(sel, text) => (document.querySelector(sel)?.textContent || '').includes(text)", args: new object[] { selector, text });

    private static async Task WaitForTextNotAsync(IPage page, string selector, string text) =>
        await page.WaitForFunctionAsync("(sel, text) => { const value = (document.querySelector(sel)?.textContent || '').trim(); return value && value !== text; }", args: new object[] { selector, text });

    private static Task<string> HashAsync(IPage page) => page.EvaluateExpressionAsync<string>("location.hash");

    private static async Task SeedOrderAsync(string dbPath, string code, string caseName, string clinicCode, string requestedDeliveryDate)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        await using (var ctx = new AppDbContext(options))
        {
            await ctx.Database.MigrateAsync();
            if (!await ctx.SchedulingClinics.AnyAsync(c => c.Code == clinicCode))
            {
                ctx.SchedulingClinics.Add(new SchedulingClinicEntity
                {
                    Code = clinicCode,
                    DisplayName = clinicCode == "DEMO" ? "Demo Dental Clinic" : "Other Clinic",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await ctx.SaveChangesAsync();
            }
        }

        var repo = new SqliteOrderRepo(() => new AppDbContext(options));
        await repo.CreateOrderAsync(new OrderRecord(
            0,
            code,
            clinicCode,
            clinicCode == "DEMO" ? "Demo Dental Clinic" : "Other Clinic",
            clinicCode == "DEMO" ? "assistant-1" : "other-1",
            clinicCode == "DEMO" ? "Assistant 1" : "Other Member 1",
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
    }

    private sealed class BrowserBridgeServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _inner;

        private BrowserBridgeServer(WebApplication app, HttpClient inner, int port)
        {
            _app = app;
            _inner = inner;
            BaseUrl = $"http://127.0.0.1:{port}";
        }

        private string BaseUrl { get; }

        public string Url(string pathAndQuery) => BaseUrl + pathAndQuery;

        public static async Task<BrowserBridgeServer> StartAsync(ApiTestFixture fixture)
        {
            var port = GetFreePort();
            var inner = fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.Logging.ClearProviders();
            var app = builder.Build();
            var server = new BrowserBridgeServer(app, inner, port);
            app.Run(ctx => server.ForwardAsync(ctx));
            await app.StartAsync();
            return server;
        }

        private async Task ForwardAsync(HttpContext context)
        {
            var target = new Uri(_inner.BaseAddress!, $"{context.Request.Path}{context.Request.QueryString}");
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
            if (HasBody(context.Request.Method))
                request.Content = new StreamContent(context.Request.Body);

            foreach (var header in context.Request.Headers)
            {
                if (ShouldSkipRequestHeader(header.Key)) continue;
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content != null)
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            using var response = await _inner.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers)
            {
                if (!ShouldSkipResponseHeader(header.Key))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in response.Content.Headers)
            {
                if (!ShouldSkipResponseHeader(header.Key))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            context.Response.Headers.Remove("transfer-encoding");
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            _inner.Dispose();
        }

        private static bool HasBody(string method) =>
            HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

        private static bool ShouldSkipRequestHeader(string name) =>
            string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Upgrade", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Proxy-Connection", StringComparison.OrdinalIgnoreCase);

        private static bool ShouldSkipResponseHeader(string name) =>
            string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase);

        private static int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
