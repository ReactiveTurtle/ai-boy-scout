using System.Text.Json;
using BoyScout.Infrastructure.Configuration;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class BoyScoutConfigServiceTests
{
    private static readonly FirebaseConnectionInput Credentials = new(
        "https://example.firebaseio.com", "project-1", "user@example.com", "secret-private-key");

    [Fact]
    public void Persists_identity_name_and_backend_across_instances_without_exposing_credentials_in_status()
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        var initial = service.LoadStatus();
        Assert.NotEqual(Guid.Empty, initial.InstallationId);
        Assert.Equal("BoyScout", initial.DisplayName);
        Assert.Equal(StorageBackend.Sqlite, initial.Backend);
        Assert.False(initial.HasFirebaseConnection);
        Assert.Null(service.LoadFirebaseConnection());
        Assert.Throws<InvalidOperationException>(() => service.SelectBackend(StorageBackend.Firebase));

        service.SetDisplayName("My scout");
        var saved = service.SaveConnection(Credentials);
        Assert.Equal(StorageBackend.Sqlite, saved.Backend);
        Assert.True(saved.HasFirebaseConnection);
        service.SelectBackend(StorageBackend.Firebase);

        var reopened = new BoyScoutConfigService(fixture.Path);
        var status = reopened.LoadStatus();
        Assert.Equal(initial.InstallationId, status.InstallationId);
        Assert.Equal("My scout", status.DisplayName);
        Assert.Equal(StorageBackend.Firebase, status.Backend);
        Assert.True(status.HasFirebaseConnection);
        var json = JsonSerializer.Serialize(status);
        Assert.DoesNotContain(Credentials.PrivateKey, json);
        Assert.DoesNotContain(Credentials.ClientEmail, json);
        Assert.DoesNotContain(Credentials.DatabaseUrl, json);

        var connection = reopened.LoadFirebaseConnection();
        Assert.NotNull(connection);
        Assert.Equal(Credentials.PrivateKey, connection.PrivateKey);
        Assert.Equal(Credentials.ProjectId, connection.ProjectId);
        reopened.SelectBackend(StorageBackend.Sqlite);
        Assert.Null(reopened.LoadFirebaseConnection());
        reopened.SelectBackend(StorageBackend.Firebase);
        Assert.Equal(Credentials.PrivateKey, reopened.LoadFirebaseConnection()!.PrivateKey);

        var disk = File.ReadAllText(fixture.Path);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain(Credentials.PrivateKey, disk);
        else
        {
            Assert.Contains(Credentials.PrivateKey, disk);
            Assert.Equal(0, (int)(File.GetUnixFileMode(fixture.Path) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead
                | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)));
        }
    }

    [Fact]
    public void Invalid_updates_do_not_erase_existing_settings()
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        service.SaveConnection(Credentials);
        service.SelectBackend(StorageBackend.Firebase);
        var before = File.ReadAllText(fixture.Path);

        Assert.Throws<ArgumentException>(() => service.SaveConnection(Credentials with { DatabaseUrl = "http://insecure.example" }));
        Assert.Throws<ArgumentException>(() => service.SetDisplayName("\n"));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.SelectBackend((StorageBackend)42));
        Assert.Equal(before, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void Corruption_fails_closed_without_overwriting_file()
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        service.LoadStatus();
        File.WriteAllText(fixture.Path, "{not valid json");
        Assert.Throws<InvalidOperationException>(() => service.LoadStatus());
        Assert.Throws<InvalidOperationException>(() => service.SaveConnection(Credentials));
        Assert.Throws<InvalidOperationException>(() => service.SelectBackend(StorageBackend.Sqlite));
        Assert.Equal("{not valid json", File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void Explicit_firebase_selection_without_credentials_never_falls_back_to_sqlite()
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        service.LoadStatus();
        var contents = File.ReadAllText(fixture.Path).Replace("\"Backend\":0", "\"Backend\":1");
        Assert.Contains("\"Backend\":1", contents);
        File.WriteAllText(fixture.Path, contents);

        Assert.Throws<InvalidOperationException>(() => service.LoadStatus());
        Assert.Throws<InvalidOperationException>(() => service.LoadFirebaseConnection());
        Assert.Equal(contents, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void Unsafe_existing_unix_permissions_are_rejected()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        service.LoadStatus();
        File.SetUnixFileMode(fixture.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.Throws<InvalidOperationException>(() => service.LoadStatus());
        Assert.Throws<InvalidOperationException>(() => service.SaveConnection(Credentials));
    }

    [Fact]
    public void Bindings_persist_and_resolve_by_id_and_normalized_path_with_consent_off_by_default()
    {
        using var fixture = new ConfigFixture();
        var directory = fixture.CreateProjectDirectory("project");
        var second = fixture.CreateProjectDirectory("second");
        var service = new BoyScoutConfigService(fixture.Path);
        service.LoadStatus();
        var original = File.ReadAllText(fixture.Path).Replace(",\"Bindings\":[]", "");
        File.WriteAllText(fixture.Path, original); // Simulate a config written before bindings existed.
        Assert.Empty(service.ListBindings());

        var bound = service.Bind(123, System.IO.Path.Combine(directory, "."));
        Assert.Equal(new ProjectBinding(123, directory, false), bound);
        Assert.Equal(bound, new BoyScoutConfigService(fixture.Path).ResolveByProjectId(123));
        Assert.Equal(bound, service.ResolveByPath(directory + System.IO.Path.DirectorySeparatorChar));
        Assert.Null(service.ResolveByProjectId(999));
        Assert.Null(service.ResolveByPath(second));
        Assert.Equal(bound, Assert.Single(service.ListBindings()));
        Assert.Equal(bound with { AiReadConsent = true }, service.SetAiReadConsent(123, true));
        Assert.True(new BoyScoutConfigService(fixture.Path).ResolveByPath(directory)!.AiReadConsent);
        Assert.Equal(bound with { AiReadConsent = true }, service.Bind(123, directory));
        Assert.Equal(bound, service.SetAiReadConsent(123, false));
        Assert.True(service.Unbind(123));
        Assert.False(service.Unbind(123));
        Assert.Empty(new BoyScoutConfigService(fixture.Path).ListBindings());
    }

    [Fact]
    public void Firebase_write_selection_defaults_off_requires_credentials_and_consent_and_resets_on_rebind()
    {
        using var fixture = new ConfigFixture();
        var first = fixture.CreateProjectDirectory("first");
        var second = fixture.CreateProjectDirectory("second");
        var service = new BoyScoutConfigService(fixture.Path);
        service.LoadStatus();
        var binding = service.Bind(17, first);
        Assert.False(binding.FirebaseWriteEnabled);
        Assert.Throws<InvalidOperationException>(() => service.SetFirebaseWriteEnabled(17, true));

        service.SaveConnection(Credentials);
        service.Bind(17, first);
        Assert.Throws<InvalidOperationException>(() => service.SetFirebaseWriteEnabled(17, true));
        service.SetAiReadConsent(17, true);
        var enabled = service.SetFirebaseWriteEnabled(17, true);
        Assert.True(enabled.FirebaseWriteEnabled);
        Assert.True(new BoyScoutConfigService(fixture.Path).ResolveByProjectId(17)!.FirebaseWriteEnabled);

        Directory.Delete(first);
        Assert.Throws<DirectoryNotFoundException>(() => service.SetFirebaseWriteEnabled(17, true));
        Assert.True(service.ResolveByProjectId(17)!.FirebaseWriteEnabled);

        var disabled = service.SetFirebaseWriteEnabled(17, false);
        Assert.False(disabled.FirebaseWriteEnabled);
        Directory.CreateDirectory(first);
        service.SetFirebaseWriteEnabled(17, true);
        var rebound = service.Bind(17, second);
        Assert.False(rebound.FirebaseWriteEnabled);
        Assert.False(rebound.AiReadConsent);

        var oldJson = File.ReadAllText(fixture.Path).Replace(",\"FirebaseWriteEnabled\":false", "", StringComparison.Ordinal);
        File.WriteAllText(fixture.Path, oldJson);
        Assert.False(new BoyScoutConfigService(fixture.Path).ResolveByProjectId(17)!.FirebaseWriteEnabled);
    }

    [Fact]
    public void Rejects_duplicate_or_missing_directory_without_changing_bindings_and_rebinding_revokes_consent()
    {
        using var fixture = new ConfigFixture();
        var first = fixture.CreateProjectDirectory("first");
        var second = fixture.CreateProjectDirectory("second");
        var service = new BoyScoutConfigService(fixture.Path);
        service.Bind(1, first);
        service.SetAiReadConsent(1, true);
        var before = File.ReadAllText(fixture.Path);
        Assert.Throws<InvalidOperationException>(() => service.Bind(2, first));
        Assert.Throws<DirectoryNotFoundException>(() => service.Bind(2, System.IO.Path.Combine(first, "missing")));
        Assert.Throws<ArgumentException>(() => service.Bind(2, "relative"));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Bind(0, second));
        Assert.Throws<KeyNotFoundException>(() => service.SetAiReadConsent(2, true));
        Assert.Equal(before, File.ReadAllText(fixture.Path));

        Assert.Equal(new ProjectBinding(1, second, false), service.Bind(1, second));
        Assert.Null(service.ResolveByPath(first));
        Assert.False(service.ResolveByProjectId(1)!.AiReadConsent);
    }

    [Fact]
    public void Credentials_for_another_database_or_firebase_project_clear_bindings_only()
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        var directory = fixture.CreateProjectDirectory("project");
        service.SaveConnection(Credentials);
        service.Bind(1, directory);
        service.SetAiReadConsent(1, true);
        service.SaveConnection(Credentials with { ClientEmail = "another@example.com", PrivateKey = "other-key" });
        Assert.True(service.ResolveByProjectId(1)!.AiReadConsent);
        service.SaveConnection(Credentials with { DatabaseUrl = "https://other.firebaseio.com" });
        Assert.Empty(service.ListBindings());
        service.Bind(1, directory);
        service.SetAiReadConsent(1, true);
        service.SaveConnection(Credentials with { ProjectId = "project-2" });
        Assert.Empty(service.ListBindings());
    }

    [Fact]
    public void Same_database_url_spelling_preserves_bindings_but_missing_path_cannot_receive_consent()
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        var directory = fixture.CreateProjectDirectory("project");
        service.SaveConnection(Credentials);
        service.Bind(1, directory);
        service.SaveConnection(Credentials with { DatabaseUrl = Credentials.DatabaseUrl + "/" });
        Assert.NotNull(service.ResolveByProjectId(1));

        Directory.Delete(directory);
        Assert.Throws<DirectoryNotFoundException>(() => service.SetAiReadConsent(1, true));
        Assert.False(service.ResolveByProjectId(1)!.AiReadConsent);
    }

    [Theory]
    [InlineData("[{\"ProjectId\":1,\"LocalPath\":\"relative\",\"AiReadConsent\":true}]")]
    [InlineData("[{\"ProjectId\":0,\"LocalPath\":\"relative\",\"AiReadConsent\":true}]")]
    [InlineData("null")]
    public void Invalid_bindings_fail_closed(string bindings)
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        service.LoadStatus();
        var broken = File.ReadAllText(fixture.Path).TrimEnd('}') + ",\"Bindings\":" + bindings + "}";
        File.WriteAllText(fixture.Path, broken);
        Assert.Throws<InvalidOperationException>(() => service.ListBindings());
        Assert.Throws<InvalidOperationException>(() => service.Bind(1, fixture.CreateProjectDirectory("project")));
        Assert.Equal(broken, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void Duplicate_binding_ids_and_paths_in_config_fail_closed()
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        var first = fixture.CreateProjectDirectory("first");
        var second = fixture.CreateProjectDirectory("second");
        service.Bind(1, first);
        service.Bind(2, second);
        var json = File.ReadAllText(fixture.Path);
        File.WriteAllText(fixture.Path, json.Replace("\"ProjectId\":2", "\"ProjectId\":1"));
        Assert.Throws<InvalidOperationException>(() => service.ListBindings());
        File.WriteAllText(fixture.Path, json.Replace(JsonSerializer.Serialize(second), JsonSerializer.Serialize(first)));
        Assert.Throws<InvalidOperationException>(() => service.ListBindings());
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(fixture.Path, json.Replace(JsonSerializer.Serialize(second), JsonSerializer.Serialize(first.ToUpperInvariant())));
            Assert.Throws<InvalidOperationException>(() => service.ListBindings());
        }
    }

    [Fact]
    public void Path_comparison_uses_platform_case_rules()
    {
        using var fixture = new ConfigFixture();
        var service = new BoyScoutConfigService(fixture.Path);
        var directory = fixture.CreateProjectDirectory("lowercase");
        service.Bind(1, directory);
        var uppercase = directory.ToUpperInvariant();
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(1, service.ResolveByPath(uppercase)!.ProjectId);
            Assert.Throws<InvalidOperationException>(() => service.Bind(2, uppercase));
        }
        else Assert.Null(service.ResolveByPath(uppercase));
    }

    private sealed class ConfigFixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "boy-scout-config-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(directory, "config.json");
        public string CreateProjectDirectory(string name)
        {
            var project = System.IO.Path.Combine(directory, name);
            Directory.CreateDirectory(project);
            return project;
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
