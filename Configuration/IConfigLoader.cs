namespace Configuration;

public interface IConfigLoader
{
    public Task<Config> LoadAsync();
}