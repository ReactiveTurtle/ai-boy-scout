namespace BoyScout.Domain.Projects;

public sealed class BoyScoutProject
{
    private readonly List<BoyScoutProjectPath> paths = [];

    private BoyScoutProject() { }

    private BoyScoutProject(string name, DateTimeOffset createdAt)
    {
        Name = ValidateName(name);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public long Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<BoyScoutProjectPath> Paths => paths.AsReadOnly();
    public string? PrimaryPath => paths.SingleOrDefault(path => path.IsPrimary)?.Path;

    public static BoyScoutProject Create(string name, DateTimeOffset createdAt) => new(name, createdAt);

    public void Rename(string name, DateTimeOffset updatedAt)
    {
        Name = ValidateName(name);
        UpdatedAt = updatedAt;
    }

    public BoyScoutProjectPath AddPath(string path, bool isPrimary, DateTimeOffset createdAt)
    {
        var normalized = BoyScoutProjectPath.Normalize(path);
        if (paths.Any(existing => string.Equals(existing.Path, normalized, PathComparison)))
            throw new InvalidOperationException("This path is already linked to the project.");
        if (isPrimary)
            foreach (var existing in paths) existing.SetPrimary(false);
        var newPath = new BoyScoutProjectPath(normalized, isPrimary || paths.Count == 0, createdAt);
        paths.Add(newPath);
        UpdatedAt = createdAt;
        return newPath;
    }

    public void SetPrimaryPath(string path, DateTimeOffset updatedAt)
    {
        var normalized = BoyScoutProjectPath.Normalize(path);
        var selected = paths.SingleOrDefault(existing => string.Equals(existing.Path, normalized, PathComparison))
            ?? throw new ArgumentException("The path is not linked to this project.", nameof(path));
        foreach (var existing in paths) existing.SetPrimary(ReferenceEquals(existing, selected));
        UpdatedAt = updatedAt;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string ValidateName(string name)
    {
        var value = name?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            throw new ArgumentException("Project name must contain 1-200 characters.", nameof(name));
        return value;
    }
}
