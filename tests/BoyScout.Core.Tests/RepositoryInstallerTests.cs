using System.Diagnostics;
using System.Text.Json.Nodes;
using BoyScout.Api.Setup;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class RepositoryInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "boy-scout-setup-" + Guid.NewGuid().ToString("N"));
    private readonly RepositoryInstaller installer;

    public RepositoryInstallerTests()
    {
        Directory.CreateDirectory(root);
        using var git = Process.Start(new ProcessStartInfo("git") { ArgumentList = { "init", "-q", root }, UseShellExecute = false });
        git!.WaitForExit();
        Assert.Equal(0, git.ExitCode);
        var dll = Path.Combine(root, "BoyScout.Mcp.dll");
        File.WriteAllText(dll, "test fixture");
        installer = new RepositoryInstaller(dll);
    }

    [Fact]
    public void Preview_does_not_write_and_confirm_preserves_unrelated_config()
    {
        var config = Path.Combine(root, "opencode.json");
        File.WriteAllText(config, "{\"theme\":\"dark\",\"mcp\":{\"other\":{\"type\":\"remote\"}}}");
        var before = File.ReadAllText(config);
        var preview = installer.Preview(root);
        Assert.Equal(before, File.ReadAllText(config));
        Assert.False(File.Exists(Path.Combine(root, OperatingSystem.IsWindows() ? "boy-scout.cmd" : "boy-scout.sh")));

        installer.Install(root, preview.Token);

        var updated = JsonNode.Parse(File.ReadAllText(config))!;
        Assert.Equal("dark", (string?)updated["theme"]);
        Assert.Equal("remote", (string?)updated["mcp"]!["other"]!["type"]);
        Assert.Equal("local", (string?)updated["mcp"]!["boy-scout"]!["type"]);
        var exclude = File.ReadAllText(Path.Combine(root, ".git", "info", "exclude"));
        Assert.Contains("/opencode.json", exclude);
        Assert.Contains(OperatingSystem.IsWindows() ? "/boy-scout.cmd" : "/boy-scout.sh", exclude);
        var shortcut = File.ReadAllText(Path.Combine(root, OperatingSystem.IsWindows() ? "boy-scout.cmd" : "boy-scout.sh"));
        Assert.Contains(OperatingSystem.IsWindows() ? "%%" : "%", shortcut);
    }

    [Fact]
    public void Confirm_rejects_changes_after_preview()
    {
        var preview = installer.Preview(root);
        File.WriteAllText(Path.Combine(root, "opencode.json"), "{}");
        Assert.Throws<InvalidOperationException>(() => installer.Install(root, preview.Token));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(root, "opencode.json")));
    }

    [Fact]
    public void Existing_mcp_or_shortcut_is_not_overwritten()
    {
        var config = Path.Combine(root, "opencode.json");
        File.WriteAllText(config, "{\"mcp\":{\"boy-scout\":{}}}");
        Assert.Throws<InvalidOperationException>(() => installer.Preview(root));
        File.Delete(config);
        var shortcut = Path.Combine(root, OperatingSystem.IsWindows() ? "boy-scout.cmd" : "boy-scout.sh");
        File.WriteAllText(shortcut, "keep");
        Assert.Throws<InvalidOperationException>(() => installer.Preview(root));
        Assert.Equal("keep", File.ReadAllText(shortcut));
    }

    [Fact]
    public void Path_must_be_git_root()
    {
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        Assert.Throws<ArgumentException>(() => installer.Preview(nested));
    }

    [Fact]
    public void Tracked_config_is_rejected()
    {
        File.WriteAllText(Path.Combine(root, "opencode.json"), "{}");
        using var git = Process.Start(new ProcessStartInfo("git") { WorkingDirectory = root, ArgumentList = { "add", "opencode.json" }, UseShellExecute = false });
        git!.WaitForExit();
        Assert.Equal(0, git.ExitCode);
        Assert.Throws<InvalidOperationException>(() => installer.Preview(root));
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
