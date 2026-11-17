using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Nook.Api.Storage;

namespace Nook.Api.Tests.Unit;

public sealed class SettingsFileTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "nook-settings-tests", Guid.NewGuid().ToString("N"));

    public SettingsFileTests() => Directory.CreateDirectory(_dataPath);

    public void Dispose() => Directory.Delete(_dataPath, recursive: true);

    private ConfigurationManager Configuration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Nook:DataPath"] = _dataPath });
        return configuration;
    }

    private string WriteFile(string json)
    {
        var path = Path.Combine(_dataPath, "nook.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Settings_in_the_file_are_read()
    {
        var path = WriteFile("""
            {
              // Comments are allowed.
              "Nook": { "Uploads": { "MaxBytes": 1024 } },
              "RateLimiting": { "Anonymous": { "PermitLimit": 5 } }
            }
            """);
        var configuration = Configuration();

        var added = configuration.AddSettingsFile();

        Assert.Equal(path, added);
        Assert.Equal("1024", configuration["Nook:Uploads:MaxBytes"]);
        Assert.Equal("5", configuration["RateLimiting:Anonymous:PermitLimit"]);
    }

    [Fact]
    public void Data_root_without_the_file_adds_nothing()
    {
        var configuration = Configuration();
        var sources = configuration.Sources.Count;

        Assert.Null(configuration.AddSettingsFile());
        Assert.Equal(sources, configuration.Sources.Count);
    }

    [Fact]
    public void No_data_root_adds_nothing()
    {
        WriteFile("{}");

        Assert.Null(new ConfigurationManager().AddSettingsFile());
    }

    [Fact]
    public void Environment_variable_wins_over_the_file()
    {
        WriteFile("""{ "Nook": { "Uploads": { "MaxBytes": 1024 } } }""");
        var configuration = Configuration();
        configuration.AddEnvironmentVariables();
        // Stands in for whatever is added after the environment variables, such as the command line.
        configuration.AddInMemoryCollection();

        configuration.AddSettingsFile();

        var sources = configuration.Sources;
        var environment = sources.IndexOf(sources.Single(source => source is EnvironmentVariablesConfigurationSource));
        Assert.Equal(environment - 1, sources.IndexOf(sources.Single(source => source is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource)));
    }

    [Fact]
    public void File_that_is_not_json_fails_and_names_the_file()
    {
        var path = WriteFile("{ not json");

        var exception = Assert.ThrowsAny<Exception>(() => Configuration().AddSettingsFile());

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }
}
