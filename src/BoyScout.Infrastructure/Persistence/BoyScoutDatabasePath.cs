namespace BoyScout.Infrastructure.Persistence;

public static class BoyScoutDatabasePath
{
    public static string Resolve()
    {
        var configuredPath = Environment.GetEnvironmentVariable("BOY_SCOUT_DB");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "boy-scout-mcp",
            "reports.db");
    }
}
