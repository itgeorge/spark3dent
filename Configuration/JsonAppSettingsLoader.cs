using Microsoft.Extensions.Configuration;

namespace Configuration;

public class JsonAppSettingsLoader : IConfigLoader
{
    public const string AppSettingsFileName = "appsettings.json";
    private readonly string _basePath;

    /// <param name="basePath">Directory containing appsettings.json. If null, uses <see cref="AppContext.BaseDirectory"/>.</param>
    public JsonAppSettingsLoader(string? basePath = null)
    {
        _basePath = basePath ?? AppContext.BaseDirectory;
    }

    /// <summary>Returns the full path to appsettings.json in the configured base directory.</summary>
    public string GetAppSettingsPath() => Path.Combine(_basePath, AppSettingsFileName);

    public async Task<Config> LoadAsync()
    {
        var configPath = GetAppSettingsPath();
        if (!File.Exists(configPath))
            throw new InvalidOperationException($"Configuration file not found: {configPath}. Expected {AppSettingsFileName} in the application directory.");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(_basePath)
            .AddJsonFile(AppSettingsFileName, optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var config = new Config();
        configuration.Bind(config);
        config.Desktop ??= new DesktopConfig();
        return await Task.FromResult(config);
    }
}
