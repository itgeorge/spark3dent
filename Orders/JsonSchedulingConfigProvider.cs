using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orders;

public sealed class JsonSchedulingConfigProvider : ISchedulingConfigProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;
    private SchedulingConfigSnapshot _current;

    public JsonSchedulingConfigProvider(string path)
    {
        _path = path;
        _current = Load(path);
    }

    public SchedulingConfigSnapshot Current => _current;

    public Task<SchedulingConfigSnapshot> ReloadAsync(CancellationToken ct = default)
    {
        _current = Load(_path);
        return Task.FromResult(_current);
    }

    private static SchedulingConfigSnapshot Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Scheduling config file not found: {path}");

        var json = File.ReadAllText(path);
        var options = JsonSerializer.Deserialize<SchedulingOptions>(json, JsonOptions)
            ?? throw new InvalidOperationException("Scheduling config is empty or invalid.");

        SchedulingConfigValidator.Validate(options);
        return new SchedulingConfigSnapshot(options, DateTimeOffset.UtcNow, path);
    }
}
