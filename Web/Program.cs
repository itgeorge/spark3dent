using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AppSetup;
using Utilities;

var config = await AppBootstrap.LoadAndResolveConfigAsync();

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

// Find a free port on localhost
int port;
using (var listener = new TcpListener(IPAddress.Loopback, 0))
{
    listener.Start();
    port = ((IPEndPoint)listener.LocalEndpoint).Port;
}

var url = $"http://127.0.0.1:{port}";

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(url);

var app = builder.Build();

// Serve embedded UI at GET /
var webAssembly = Assembly.GetExecutingAssembly();
app.MapGet("/", async () =>
{
    var html = await EmbeddedResourceLoader.LoadEmbeddedResourceAsync("index.html", webAssembly);
    return Results.Content(html, "text/html; charset=utf-8");
});

await app.StartAsync();

Console.WriteLine($"Spark3Dent Web running at {url}");
Console.WriteLine("Press Ctrl+C to stop.");

Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

await app.WaitForShutdownAsync();
