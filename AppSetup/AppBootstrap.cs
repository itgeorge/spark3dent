using System.Text.Json;
using Accounting;
using Configuration;
using Database;
using Invoices;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;
using Storage;
using Utilities;

namespace AppSetup;

public static class AppBootstrap
{
    private const string InvoicesBucket = "invoices";
    private const int InvoiceNumberPadding = 10;

    /// <summary>Loads config via JsonAppSettingsLoader, resolves default Desktop paths, writes back defaults if needed. Returns Config.</summary>
    public static async Task<Config> LoadAndResolveConfigAsync(string? basePath = null)
    {
        var loader = new JsonAppSettingsLoader(basePath);
        var config = await loader.LoadAsync();

        var defaultsUsed = false;
        var desktop = config.Desktop;

        if (string.IsNullOrEmpty(desktop.DatabasePath))
        {
            desktop.DatabasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spark3Dent", "spark3dent.db");
            defaultsUsed = true;
        }

        if (string.IsNullOrEmpty(desktop.BlobStoragePath))
        {
            desktop.BlobStoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Spark3Dent");
            defaultsUsed = true;
        }

        if (string.IsNullOrEmpty(desktop.LogDirectory))
        {
            desktop.LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spark3Dent", "logs");
            defaultsUsed = true;
        }

        if (defaultsUsed)
        {
            var appSettingsPath = loader.GetAppSettingsPath();
            var json = JsonSerializer.Serialize(new { App = config.App, Desktop = desktop },
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(appSettingsPath, json);
        }

        return config;
    }

    /// <summary>Creates DB context, runs migrations, creates repos/exporters/blob storage/InvoiceManagement with logging wrappers.</summary>
    public static async Task<SetupResult?> SetupDependenciesAsync(Config config, ILogger logger)
    {
        if (config.App.SellerAddress == null || config.App.SellerBankTransferInfo == null)
            return null;

        var sellerAddress = ConfigToBillingAddress(config.App.SellerAddress);
        var bankTransferInfo = ConfigToBankTransferInfo(config.App.SellerBankTransferInfo);

        var dbPath = config.Desktop.DatabasePath;
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir))
            Directory.CreateDirectory(dbDir);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using (var ctx = new AppDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        AppDbContext ContextFactory() => new AppDbContext(options);
        var invoiceRepo = new SqliteInvoiceRepo(ContextFactory, config);
        var clientRepo = new SqliteClientRepo(ContextFactory);

        var transcriber = new BgAmountTranscriber();
        var template = await InvoiceHtmlTemplate.LoadAsync(transcriber, invoiceNumberPadding: InvoiceNumberPadding);

        var chromiumPath = await ResolveChromiumExecutablePathAsync();
        var pdfExporter = new InvoicePdfExporter(chromiumPath);
        var imageExporter = new InvoiceImageExporter(chromiumPath);

        var loggingInvoiceRepo = new LoggingInvoiceRepo(invoiceRepo, logger);
        var loggingClientRepo = new LoggingClientRepo(clientRepo, logger);
        var loggingPdfExporter = new LoggingInvoiceExporter(pdfExporter, logger);
        var loggingImageExporter = new LoggingInvoiceExporter(imageExporter, logger);
        var contentTypeMap = BuildContentTypeMap(loggingPdfExporter, loggingImageExporter);
        var blobStorage = new LocalFileSystemBlobStorage(contentTypeMap);
        var invoicesDir = Path.Combine(config.Desktop.BlobStoragePath, "invoices");
        blobStorage.DefineBucket(InvoicesBucket, invoicesDir);
        var loggingBlobStorage = new LoggingBlobStorage(blobStorage, logger);

        var invoiceManagement = new InvoiceManagement(
            loggingInvoiceRepo,
            loggingClientRepo,
            template,
            loggingBlobStorage,
            sellerAddress,
            bankTransferInfo,
            InvoicesBucket,
            logger,
            invoiceNumberPadding: InvoiceNumberPadding);

        return new SetupResult(
            invoiceManagement,
            loggingClientRepo,
            loggingPdfExporter,
            loggingImageExporter,
            config);
    }

    public static async Task<string?> ResolveChromiumExecutablePathAsync()
    {
        var chromiumDir = Path.Combine(AppContext.BaseDirectory, "chromium");
        if (!Directory.Exists(chromiumDir))
            return null;

        var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = chromiumDir });
        try
        {
            var result = await fetcher.DownloadAsync();
            return result.GetExecutablePath();
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string> BuildContentTypeMap(IInvoiceExporter pdfExporter, IInvoiceExporter imageExporter)
    {
        var mimeToExtension = new Dictionary<string, string>
        {
            ["application/pdf"] = ".pdf",
            ["image/png"] = ".png"
        };
        var map = new Dictionary<string, string>();
        foreach (var exporter in new[] { pdfExporter, imageExporter })
        {
            if (mimeToExtension.TryGetValue(exporter.MimeType, out var ext))
                map[exporter.MimeType] = ext;
        }
        return map;
    }

    public static BillingAddress ConfigToBillingAddress(SellerAddress sa) =>
        new(sa.Name, sa.RepresentativeName, sa.CompanyIdentifier, sa.VatIdentifier, sa.Address, sa.City, sa.PostalCode, sa.Country);

    public static BankTransferInfo ConfigToBankTransferInfo(SellerBankTransferInfo st) =>
        new(st.Iban, st.BankName, st.Bic);

    public record SetupResult(
        InvoiceManagement InvoiceManagement,
        IClientRepo ClientRepo,
        IInvoiceExporter PdfExporter,
        IInvoiceExporter ImageExporter,
        Config Config);
}
