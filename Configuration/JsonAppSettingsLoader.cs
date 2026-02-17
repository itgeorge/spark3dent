namespace Configuration;

public class JsonAppSettingsLoader : IConfigLoader
{
    // TODO: implement a config loader based on an appsettings.json file expected in the app directory
    //  - use Microsoft.Extensions.Configuration
    //  - use AddEnvironmentVariables() for future-proofing for when we move this to Google Cloud Run
    
    public Task<Config> LoadAsync()
    {
        throw new NotImplementedException();
    }
}