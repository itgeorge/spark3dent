using Utilities;

namespace Configuration;

public class LoggingConfigLoader : IConfigLoader
{
    private readonly IConfigLoader _inner;
    private readonly ILogger _logger;

    public LoggingConfigLoader(IConfigLoader inner, ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = new SafeLogger(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public async Task<Config> LoadAsync()
    {
        _logger.LogInfo("ConfigLoader.LoadAsync");
        try
        {
            var config = await _inner.LoadAsync();
            _logger.LogInfo("ConfigLoader.LoadAsync completed");
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError("ConfigLoader.LoadAsync failed", ex);
            throw;
        }
    }
}
