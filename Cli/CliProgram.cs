using System.Text.Json;
using Configuration;
using Utilities;

namespace Cli;

class CliProgram
{
    private const int LogBufferSize = 20;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var config = await LoadAndResolveConfigAsync();

        // Set up logging: file in LogDirectory, buffered for performance
        var logDir = config.Desktop.LogDirectory;
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "spark3dent.log");
        // FileLogger uses append mode; BufferedLogger flushes on Dispose
        using var fileLogger = new FileLogger(logPath);
        using var logger = new BufferedLogger(fileLogger, LogBufferSize);
        logger.LogInfo("Spark3Dent started");

        // TODO (Phase 9): initialize dependencies (repos, exporter, blob storage, etc.) and wrap with logging decorators:
        // LoggingInvoiceRepo, LoggingClientRepo, LoggingInvoiceExporter, LoggingBlobStorage, LoggingConfigLoader

        // Run in a loop until exit.
        // - If args are provided, run based on them
        // - Otherwise, go into "interactive mode", prompt for command and parameters
        // Commands: clients add/edit/list, invoices issue/correct/list, help, exit
    }

    static async Task<Config> LoadAndResolveConfigAsync()
    {
        var loader = new JsonAppSettingsLoader();
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
}