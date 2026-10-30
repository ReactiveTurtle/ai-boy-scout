namespace BoyScout.Domain.Projects;

public sealed class BoyScoutProjectPath
{
    private BoyScoutProjectPath() { }

    internal BoyScoutProjectPath(string path, bool isPrimary, DateTimeOffset createdAt)
    {
        Path = Normalize(path);
        IsPrimary = isPrimary;
        CreatedAt = createdAt;
    }

    public long Id { get; private set; }
    public long ProjectId { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public bool IsPrimary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;

    internal static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path.Trim()))
            throw new ArgumentException("Project path must be an absolute path.", nameof(path));
        return System.IO.Path.GetFullPath(path.Trim());
    }
}
