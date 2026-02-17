namespace Configuration;

public interface IConfigLoader
{
    public Task<AppConfig> LoadAsync();
}