using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AppSetup;
using Configuration;
using Utilities;

var builder = WebApplication.CreateBuilder(args);

var config = await LoadConfigAsync(builder.Configuration);

var logDir = config.Desktop.LogDirectory;
Directory.CreateDirectory(logDir);
var logPath = Path.Combine(logDir, "spark3dent-web.log");
using var fileLogger = new FileLogger(logPath);
using var logger = new BufferedLogger(fileLogger, 20);

var setup = await AppBootstrap.SetupDependenciesAsync(config, logger);
if (setup == null)
{
    logger.LogError("Failed to initialize dependencies. Check config: SellerAddress and SellerBankTransferInfo are required.", new InvalidOperationException("Missing config"));
    throw new InvalidOperationException("SellerAddress and SellerBankTransferInfo must be configured in appsettings.json.");
}

var port = GetPort(builder.Configuration);
var url = $"http://127.0.0.1:{port}";
builder.WebHost.UseUrls(url);

var app = builder.Build();

// Global exception handling: InvalidOperationException -> 400, others -> 500, log all
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (InvalidOperationException ex)
    {
        logger.LogError($"Request failed (validation): {ex.Message}", ex);
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError($"Unexpected error: {ex.Message}", ex);
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
    }
});

var webAssembly = Assembly.GetExecutingAssembly();
app.MapGet("/", async () =>
{
    var html = await EmbeddedResourceLoader.LoadEmbeddedResourceAsync("index.html", webAssembly);
    return Results.Content(html, "text/html; charset=utf-8");
});

Web.Api.MapRoutes(app, setup);

Console.WriteLine($"Running on {url}");
await app.StartAsync();

// Development/Mvp: open browser. Test: exit immediately. Production: run until Cloud Run scales down.
var env = builder.Environment.EnvironmentName;
const string DevelopmentEnvName = "Development";
const string MvpEnvName = "Mvp";
const string TestEnvName = "Test";
var shouldOpenBrowser = config.App.ShouldOpenBrowserOnStart
    ?? (string.Equals(env, DevelopmentEnvName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(env, MvpEnvName, StringComparison.OrdinalIgnoreCase));
var shouldWaitForShutdown = !string.Equals(env, TestEnvName, StringComparison.OrdinalIgnoreCase);

if (shouldOpenBrowser)
{
    Console.WriteLine($"Spark3Dent Web running at {url}");
    Console.WriteLine("Press Ctrl+C to stop.");
    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
else
{
    Console.WriteLine("NOT auto-starting browser. To open the browser automatically, set `App.ShouldOpenBrowserOnStart: true` in appsettings.json, or set `ASPNETCORE_ENVIRONMENT=Mvp` (or Development).");
}

if (shouldWaitForShutdown)
    await app.WaitForShutdownAsync();

static int GetPort(IConfiguration configuration)
{
    var portStr = configuration["Port"] ?? Environment.GetEnvironmentVariable("PORT");
    if (int.TryParse(portStr, out var port) && port >= 0)
        return port;
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static Task<Config> LoadConfigAsync(IConfiguration configuration)
{
    var config = configuration.Get<Config>() ?? new Config();
    config.Desktop ??= new DesktopConfig();
    AppBootstrap.ResolveDesktopDefaults(config);
    return Task.FromResult(config);
}

public partial class Program { }
