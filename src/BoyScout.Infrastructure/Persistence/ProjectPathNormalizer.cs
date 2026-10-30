namespace BoyScout.Infrastructure.Persistence;

public static class ProjectPathNormalizer
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath);
        if (string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
