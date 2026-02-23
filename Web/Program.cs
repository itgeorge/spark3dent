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

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// setup.InvoiceManagement, setup.ClientRepo, etc. available for API endpoints (Phase 2/3)
app.MapGet("/", () => Results.Ok("Spark3Dent Web - Phase 1"));

app.Run();
