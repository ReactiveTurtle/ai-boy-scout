using Common.Domain;

namespace TodoGraph.Domain.Projects;

/// <summary>A workspace aggregate root whose identity is independent of its display name.</summary>
public sealed class Project : AggregateRoot
{
    private Project(string name, string fullPath)
    {
        Name = NormalizeName(name);
        FullPath = ValidateFullPath(fullPath);
    }

    public string Name { get; private set; } = string.Empty;
    public string FullPath { get; private set; } = string.Empty;

    public static Project Create(string name, string fullPath) => new(name, fullPath);

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Project name is required.", nameof(name));
        return normalized;
    }

    private static string ValidateFullPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
            throw new ArgumentException("Project full path must be an absolute path.", nameof(fullPath));
        return fullPath;
    }
}
