using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace Nook.Api.Storage;

/// <summary>The optional <c>nook.json</c> in the data root, which holds settings in one file in place of environment variables.</summary>
public static partial class SettingsFile
{
    /// <summary>The file's name inside the data root.</summary>
    public const string Name = "nook.json";

    /// <summary>
    /// Adds the settings file when the data root has one, and returns its path, or null when there is none.
    /// An environment variable wins over the file, so the data root, which says where the file is, cannot be
    /// moved by it. The file is read once: a change takes a restart.
    /// </summary>
    public static string? AddSettingsFile(this ConfigurationManager configuration)
    {
        var dataPath = configuration[StorageOptions.DataPathSetting];
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            return null;
        }

        var path = Path.Combine(Path.GetFullPath(dataPath), Name);
        if (!File.Exists(path))
        {
            return null;
        }

        var source = new JsonConfigurationSource { Path = path, Optional = false, ReloadOnChange = false };
        source.ResolveFileProvider();

        // Later sources win, so the file goes just before the environment variables.
        var sources = configuration.Sources;
        var environment = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                environment = i;
            }
        }

        if (environment < 0)
        {
            sources.Add(source);
        }
        else
        {
            sources.Insert(environment, source);
        }

        return path;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings were read from {Path}.")]
    public static partial void LogSettingsFile(ILogger logger, string path);
}
