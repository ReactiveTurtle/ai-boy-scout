namespace TodoGraph;

internal sealed record LocalDirectoryEntry(string Name, string Path, bool IsGitRepository);
internal sealed record LocalDirectoryListing(string CurrentPath, string? ParentPath, bool IsGitRepository, IReadOnlyList<LocalDirectoryEntry> Entries);

internal static class LocalDirectoryBrowser
{
    private const int EntryLimit = 500;

    public static LocalDirectoryListing List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (OperatingSystem.IsWindows())
            {
                var drives = DriveInfo.GetDrives()
                    .Select(drive => new LocalDirectoryEntry(drive.Name, drive.RootDirectory.FullName, false))
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                return new LocalDirectoryListing("", null, false, drives);
            }
            path = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ArgumentException("The requested directory path is invalid.", nameof(path), error); }

        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Directory not found or inaccessible: {fullPath}");

        try
        {
            var directory = new DirectoryInfo(fullPath);
            var entries = directory.EnumerateDirectories().Where(child =>
            {
                try { return (child.Attributes & FileAttributes.ReparsePoint) == 0; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }).Take(EntryLimit + 1).Select(child =>
            {
                var gitMarker = Path.Combine(child.FullName, ".git");
                return new LocalDirectoryEntry(child.Name, child.FullName, Directory.Exists(gitMarker) || File.Exists(gitMarker));
            }).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).Take(EntryLimit).ToArray();
            var marker = Path.Combine(directory.FullName, ".git");
            return new LocalDirectoryListing(directory.FullName, directory.Parent?.FullName,
                Directory.Exists(marker) || File.Exists(marker), entries);
        }
        catch (DirectoryNotFoundException) { throw new DirectoryNotFoundException($"Directory not found or inaccessible: {fullPath}"); }
        catch (UnauthorizedAccessException error) { throw new UnauthorizedAccessException($"Access denied while listing directory: {fullPath}", error); }
        catch (IOException error) { throw new IOException($"Could not list directory: {fullPath}", error); }
    }
}
