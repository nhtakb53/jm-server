using Microsoft.Extensions.Configuration;

namespace JmServer.Server;

public sealed class JmConfiguration
{
    private readonly IConfigurationRoot _configuration;

    private JmConfiguration(IConfigurationRoot configuration)
    {
        _configuration = configuration;
    }

    public string? this[string key] => _configuration[key];

    public static JmConfiguration Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
        return new JmConfiguration(configuration);
    }
}
