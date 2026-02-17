using System.Text.Json;
using Configuration;

namespace Cli;

class CliProgram
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var config = await LoadAndResolveConfigAsync();

        // TODO: initialize dependencies (repos, loggers, etc.)
        
        // logging: add logging to all (non-test) interface implementations, log by appending to a single file in the
        //  LogDirectory, use the available Loggers.cs and add new ones if necessary. Every implemented interface method
        //  should log info with it's name and some parameters to allow tracking overall flow (don't log all details,
        //  just something to make the logs human-readable, e.g.: for invoices log only the number). Catch unhandled
        //  exceptions at the Cli level, log errors and stacktrace and continue executing commands - an exception should
        //  generally not crash the Cli when possible.
        
        // Run in a loop until exit. 
        // - If args are provided, run based on them
        // - Otherwise, go into "interactive mode", prompt for command and parameters
        // Commands:
        // clients add (asks for client nickname, and address fields)
        // clients edit (asks for client nickname, fails if not found, then for, info and address fields, empty fields defaults to current value)
        // clients list (lists all clients, alphabetically sorted by nickname)
        // invoices issue <client nickname> <amount (handles both . and , separator)> [date (dd-MM-yyyy)] (validates amount, date is optional, defaults to today)
        // invoices correct <invoice number> <amount (handles both . and , separator)> [date (dd-MM-yyyy)] (validates number and date change consistent with other known invoices) 
        // invoices list (lists the latest invoices, sorted by date, newest first)
        // help (displays the help message)
        // exit (exits the program)
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