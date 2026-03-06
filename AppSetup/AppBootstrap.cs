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
    private const string ImportTempBucket = "invoice-import-temp";
    private const int InvoiceNumberPadding = 10;

    /// <summary>Fills empty SingleBox paths with defaults. Returns true if any were applied.</summary>
    public static bool ResolveSingleBoxDefaults(Config config)
    {
        var singleBox = config.SingleBox;
        var defaultsUsed = false;

        if (string.IsNullOrEmpty(singleBox.DatabasePath))
        {
            singleBox.DatabasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spark3Dent", "spark3dent.db");
            defaultsUsed = true;
        }

        if (string.IsNullOrEmpty(singleBox.BlobStoragePath))
        {
            singleBox.BlobStoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Spark3Dent");
            defaultsUsed = true;
        }

        if (string.IsNullOrEmpty(singleBox.LogDirectory))
        {
            singleBox.LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spark3Dent", "logs");
            defaultsUsed = true;
        }

        return defaultsUsed;
    }

    /// <summary>Loads config via JsonAppSettingsLoader, resolves default SingleBox paths, writes back defaults if needed. Returns Config.</summary>
    public static async Task<Config> LoadAndResolveConfigAsync(string? basePath = null)
    {
        var loader = new JsonAppSettingsLoader(basePath);
        var config = await loader.LoadAsync();

        if (ResolveSingleBoxDefaults(config))
        {
            var appSettingsPath = loader.GetAppSettingsPath();
            var json = JsonSerializer.Serialize(new { App = config.App, Runtime = config.Runtime, SingleBox = config.SingleBox },
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(appSettingsPath, json);
        }

        return config;
    }

    /// <summary>Creates DB context, runs migrations, creates repos/exporters/blob storage/InvoiceManagement with logging wrappers.</summary>
    public static async Task<SetupResult?> SetupDependenciesAsync(Config config, ILogger logger, string? logoBase64 = null)
    {
        if (config.App.SellerAddress == null || config.App.SellerBankTransferInfo == null)
            return null;

        var sellerAddress = ConfigToBillingAddress(config.App.SellerAddress);
        var bankTransferInfo = ConfigToBankTransferInfo(config.App.SellerBankTransferInfo);

        var dbPath = config.SingleBox.DatabasePath;
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
        var template = await InvoiceHtmlTemplate.LoadAsync(transcriber, invoiceNumberPadding: InvoiceNumberPadding, logoBase64: logoBase64);

        var chromiumPath = await ResolveChromiumExecutablePathAsync();
        var pdfExporter = new InvoicePdfExporter(chromiumPath);
        var imageExporter = new InvoiceImageExporter(chromiumPath);

        var loggingInvoiceRepo = new LoggingInvoiceRepo(invoiceRepo, logger);
        var loggingClientRepo = new LoggingClientRepo(clientRepo, logger);
        var loggingPdfExporter = new LoggingInvoiceExporter(pdfExporter, logger);
        var loggingImageExporter = new LoggingInvoiceExporter(imageExporter, logger);
        var contentTypeMap = BuildContentTypeMap(loggingPdfExporter, loggingImageExporter);
        var blobStorage = new LocalFileSystemBlobStorage(contentTypeMap);
        var invoicesDir = Path.Combine(config.SingleBox.BlobStoragePath, "invoices");
        var importTempDir = Path.Combine(config.SingleBox.BlobStoragePath, "invoice-import-temp");
        blobStorage.DefineBucket(InvoicesBucket, invoicesDir);
        blobStorage.DefineBucket(ImportTempBucket, importTempDir);
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
            loggingBlobStorage,
            ImportTempBucket,
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
        IBlobStorage BlobStorage,
        string ImportTempBucket,
        Config Config);
}
