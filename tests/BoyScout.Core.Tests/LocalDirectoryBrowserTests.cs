using TodoGraph;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class LocalDirectoryBrowserTests
{
    [Fact]
    public void ListsRootEntries()
    {
        var listing = LocalDirectoryBrowser.List(null);

        Assert.NotEmpty(listing.Entries);
        Assert.Null(listing.ParentPath);
        if (OperatingSystem.IsWindows()) Assert.All(listing.Entries, entry => Assert.True(Path.IsPathRooted(entry.Path)));
        else Assert.Equal(Path.GetPathRoot(Environment.CurrentDirectory), listing.CurrentPath);
    }

    [Fact]
    public void ListsImmediateChildrenAndDetectsGitMarkers()
    {
        var root = Path.Combine(Path.GetTempPath(), $"todo-graph-picker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var gitFolder = Directory.CreateDirectory(Path.Combine(root, "git-folder"));
            Directory.CreateDirectory(Path.Combine(gitFolder.FullName, ".git"));
            var gitFile = Directory.CreateDirectory(Path.Combine(root, "git-file"));
            File.WriteAllText(Path.Combine(gitFile.FullName, ".git"), "gitdir: elsewhere");
            Directory.CreateDirectory(Path.Combine(root, "ordinary"));

            var listing = LocalDirectoryBrowser.List(root);

            Assert.Equal(3, listing.Entries.Count);
            Assert.True(listing.Entries.Single(entry => entry.Name == "git-folder").IsGitRepository);
            Assert.True(listing.Entries.Single(entry => entry.Name == "git-file").IsGitRepository);
            Assert.False(listing.Entries.Single(entry => entry.Name == "ordinary").IsGitRepository);
            Assert.False(listing.IsGitRepository);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RejectsMissingDirectory()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() => LocalDirectoryBrowser.List(missing));
    }
}
