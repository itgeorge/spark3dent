using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Accounting;
using AppSetup;
using Configuration;
using Utilities;
using Web;

var builder = WebApplication.CreateBuilder(args);

var config = await LoadConfigAsync(builder.Configuration);

var logDir = config.SingleBox.LogDirectory;
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

builder.Services.AddSingleton(setup.Config);
builder.Services.AddSingleton(setup.ClientRepo);
builder.Services.AddSingleton<IClientRepo>(setup.ClientRepo);
builder.Services.AddSingleton(setup.BlobStorage);
builder.Services.AddSingleton<Utilities.ILogger>(logger);
builder.Services.AddSingleton<IInvoiceOperations>(new InvoiceManagementAdapter(setup.InvoiceManagement));
builder.Services.AddSingleton<IPdfInvoiceExporter>(new PdfInvoiceExporterAdapter(setup.PdfExporter));
builder.Services.AddSingleton<IImageInvoiceExporter>(new ImageInvoiceExporterAdapter(setup.ImageExporter));
builder.Services.AddSingleton<ILegacyInvoiceParser>(sp =>
    new TwoPhaseLegacyInvoiceParser(sp.GetRequiredService<IClientRepo>()));
builder.Services.AddSingleton<IInvoiceImporter>(sp =>
    new InvoiceImporter(
        sp.GetRequiredService<IClientRepo>(),
        sp.GetRequiredService<IInvoiceOperations>(),
        sp.GetRequiredService<ILegacyInvoiceParser>(),
        sp.GetRequiredService<Storage.IBlobStorage>(),
        setup.ImportTempBucket,
        sp.GetRequiredService<Utilities.ILogger>()));

var (bindAddress, port) = ResolveEndpoint(config, builder.Configuration);
var url = $"http://{bindAddress}:{port}";
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

Web.Api.MapRoutes(app);

Console.WriteLine($"Running on {url}");
await app.StartAsync();

// Development/Mvp: open browser. Test: exit immediately. Production: run until Cloud Run scales down.
var env = builder.Environment.EnvironmentName;
const string DevelopmentEnvName = "Development";
const string MvpEnvName = "Mvp";
const string TestEnvName = "Test";
var shouldOpenBrowser = config.Runtime.HostingMode == HostingMode.Desktop &&
    (config.App.ShouldOpenBrowserOnStart
     ?? (string.Equals(env, DevelopmentEnvName, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(env, MvpEnvName, StringComparison.OrdinalIgnoreCase)));
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

static (string BindAddress, int Port) ResolveEndpoint(Config config, IConfiguration configuration)
{
    var bindAddress = ResolveBindAddress(config.Runtime);
    var port = ResolvePort(config, configuration, bindAddress);
    return (bindAddress, port);
}

static string ResolveBindAddress(RuntimeConfig runtimeConfig)
{
    if (!string.IsNullOrWhiteSpace(runtimeConfig.BindAddress))
        return runtimeConfig.BindAddress;

    return runtimeConfig.HostingMode == HostingMode.Desktop ? "127.0.0.1" : "0.0.0.0";
}

static int ResolvePort(Config config, IConfiguration configuration, string bindAddress)
{
    if (config.Runtime.Port is >= 0)
        return config.Runtime.Port.Value;

    if (config.Runtime.HostingMode == HostingMode.HetznerDocker)
    {
        throw new InvalidOperationException(
            "Runtime.Port must be configured for HetznerDocker deployments.");
    }

    var portStr = configuration["PORT"] ?? Environment.GetEnvironmentVariable("PORT");
    if (int.TryParse(portStr, out var envPort) && envPort >= 0)
        return envPort;

    var listenerAddress = bindAddress == "0.0.0.0" ? IPAddress.Any : IPAddress.Loopback;
    using var listener = new TcpListener(listenerAddress, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static Task<Config> LoadConfigAsync(IConfiguration configuration)
{
    var config = configuration.Get<Config>() ?? new Config();
    config.Runtime ??= new RuntimeConfig();
    config.SingleBox ??= new SingleBoxConfig();
    AppBootstrap.ResolveSingleBoxDefaults(config);
    return Task.FromResult(config);
}

public partial class Program { }
