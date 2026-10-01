using System.Globalization;

namespace BoyScout.Api.Ai;

internal sealed record OpenCodeOptions(
    bool Enabled,
    TimeSpan Timeout,
    string Command,
    string ConfigPath,
    string RepositoryRoot)
{
    private const int DefaultTimeoutSeconds = 180;

    public static OpenCodeOptions FromEnvironment()
    {
        var enabled = ReadEnabled();
        var timeout = ReadTimeout();
        var command = Environment.GetEnvironmentVariable("BOY_SCOUT_OPENCODE_COMMAND");
        if (string.IsNullOrWhiteSpace(command))
        {
            command = "opencode";
        }

        var configuredPath = Environment.GetEnvironmentVariable("BOY_SCOUT_OPENCODE_CONFIG");
        var discoveredConfig = FindDefaultConfig();
        var repositoryRoot = discoveredConfig?.RepositoryRoot ?? FindFallbackRoot(configuredPath);
        var configPath = string.IsNullOrWhiteSpace(configuredPath)
            ? discoveredConfig?.ConfigPath ?? Path.Combine(repositoryRoot, ".opencode", "opencode.triage.json")
            : Path.GetFullPath(configuredPath, Environment.CurrentDirectory);

        if (enabled && !File.Exists(configPath))
        {
            throw new InvalidOperationException($"OpenCode config was not found: {configPath}");
        }

        return new OpenCodeOptions(enabled, timeout, command, configPath, repositoryRoot);
    }

    private static bool ReadEnabled()
    {
        var value = Environment.GetEnvironmentVariable("BOY_SCOUT_AI_ENABLED");
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!bool.TryParse(value, out var enabled))
        {
            throw new InvalidOperationException("BOY_SCOUT_AI_ENABLED must be true or false.");
        }

        return enabled;
    }

    private static TimeSpan ReadTimeout()
    {
        var value = Environment.GetEnvironmentVariable("BOY_SCOUT_AI_TIMEOUT_SECONDS");
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds is < 10 or > 900)
        {
            throw new InvalidOperationException(
                "BOY_SCOUT_AI_TIMEOUT_SECONDS must be an integer from 10 through 900.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static DiscoveredConfig? FindDefaultConfig()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startPath));
            while (directory is not null)
            {
                var configPath = Path.Combine(directory.FullName, ".opencode", "opencode.triage.json");
                if (File.Exists(configPath))
                {
                    return new DiscoveredConfig(directory.FullName, configPath);
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string FindFallbackRoot(string? configuredPath)
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startPath));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BoyScout.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configDirectory = Directory.GetParent(Path.GetFullPath(configuredPath, Environment.CurrentDirectory));
            if (configDirectory?.Name == ".opencode" && configDirectory.Parent is not null)
            {
                return configDirectory.Parent.FullName;
            }
        }

        return Environment.CurrentDirectory;
    }

    private sealed record DiscoveredConfig(string RepositoryRoot, string ConfigPath);
}
