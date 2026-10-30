using System.Collections.Concurrent;
using System.Diagnostics;

namespace BoyScout.Infrastructure.Persistence;

public static class GitRepositoryIdentity
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static string? Resolve(string projectPath)
    {
        var path = ProjectPathNormalizer.Normalize(projectPath);
        var identity = Cache.GetOrAdd(path, ResolveUncached);
        return identity.Length == 0 ? null : identity;
    }

    private static string ResolveUncached(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(path);
            startInfo.ArgumentList.Add("rev-parse");
            startInfo.ArgumentList.Add("--path-format=absolute");
            startInfo.ArgumentList.Add("--git-common-dir");

            using var process = Process.Start(startInfo);
            if (process is null) return string.Empty;
            if (!process.WaitForExit(2_000))
            {
                process.Kill(entireProcessTree: true);
                return string.Empty;
            }
            if (process.ExitCode != 0) return string.Empty;

            var commonDirectory = process.StandardOutput.ReadToEnd().Trim();
            if (commonDirectory.Length == 0) return string.Empty;
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonDirectory));
            return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or ArgumentException)
        {
            return string.Empty;
        }
    }
}
