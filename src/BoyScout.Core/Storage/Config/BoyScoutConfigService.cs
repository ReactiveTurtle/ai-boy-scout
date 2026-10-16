using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoyScout.Core.Storage.Firebase;

namespace BoyScout.Core.Storage.Config;

public enum StorageBackend { Sqlite, Firebase }

public sealed record FirebaseConnectionInput(string DatabaseUrl, string ProjectId, string ClientEmail, string PrivateKey);

// Status is safe to return to a browser; credentials are only available via LoadFirebaseConnection.
public sealed record BoyScoutConfigStatus(Guid InstallationId, string DisplayName, StorageBackend Backend, bool HasFirebaseConnection);

public sealed record ProjectBinding(long ProjectId, string LocalPath, bool AiReadConsent);

public sealed class BoyScoutConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
    };

    private readonly string path;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public BoyScoutConfigService(string? path = null)
    {
        var configuredPath = Environment.GetEnvironmentVariable("BOY_SCOUT_CONFIG");
        this.path = Path.GetFullPath(path ?? (string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Path.GetDirectoryName(BoyScoutDatabasePath.Resolve())!, "config.json")
            : configuredPath));
    }

    public BoyScoutConfigStatus LoadStatus() => WithLock(() => Status(ReadOrCreate()));

    public FirebaseConnection? LoadFirebaseConnection() => WithLock(() =>
    {
        var config = ReadOrCreate();
        if (config.Backend != StorageBackend.Firebase) return null;
        return Connection(config);
    });

    public FirebaseConnection LoadConfiguredFirebaseConnection() => WithLock(() =>
    {
        var config = ReadOrCreate();
        if (!HasConnection(config)) throw new InvalidOperationException("Configure Firebase credentials first.");
        return Connection(config);
    });

    public BoyScoutConfigStatus SaveConnection(FirebaseConnectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        // Validate before touching persisted state. The connection is saved but not selected.
        var connection = new FirebaseConnection(input.DatabaseUrl, input.ProjectId, input.ClientEmail, input.PrivateKey);
        return WithLock(() =>
        {
            var config = ReadOrCreate();
            if (!string.Equals(config.DatabaseUrl is null ? null : new Uri(config.DatabaseUrl).AbsoluteUri,
                    connection.DatabaseUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(config.ProjectId, input.ProjectId, StringComparison.Ordinal))
                config.Bindings.Clear();
            config.DatabaseUrl = connection.DatabaseUrl.AbsoluteUri;
            config.ProjectId = input.ProjectId;
            config.ClientEmail = input.ClientEmail;
            config.PrivateKey = OperatingSystem.IsWindows() ? null : input.PrivateKey;
            config.ProtectedPrivateKey = OperatingSystem.IsWindows()
                ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(input.PrivateKey), null, DataProtectionScope.CurrentUser))
                : null;
            Write(config);
            return Status(config);
        });
    }

    public BoyScoutConfigStatus SelectBackend(StorageBackend mode) => WithLock(() =>
    {
        var config = ReadOrCreate();
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == StorageBackend.Firebase && !HasConnection(config))
            throw new InvalidOperationException("Configure Firebase credentials before selecting Firebase.");
        config.Backend = mode;
        Write(config);
        return Status(config);
    });

    public BoyScoutConfigStatus SetDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100 || displayName.Any(char.IsControl))
            throw new ArgumentException("Display name must be 1-100 printable characters.", nameof(displayName));
        return WithLock(() =>
        {
            var config = ReadOrCreate();
            config.DisplayName = displayName.Trim();
            Write(config);
            return Status(config);
        });
    }

    public ProjectBinding Bind(long projectId, string localPath)
    {
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        var normalized = NormalizePath(localPath);
        return WithLock(() =>
        {
            var config = ReadOrCreate();
            if (!Directory.Exists(normalized)) throw new DirectoryNotFoundException($"Project directory does not exist: {normalized}");
            if (config.Bindings.Any(binding => binding.ProjectId != projectId && PathComparer.Equals(binding.LocalPath, normalized)))
                throw new InvalidOperationException("Directory is already bound to another project.");
            var current = config.Bindings.FindIndex(binding => binding.ProjectId == projectId);
            var binding = new ProjectBinding(projectId, normalized,
                current >= 0 && PathComparer.Equals(config.Bindings[current].LocalPath, normalized)
                    && config.Bindings[current].AiReadConsent);
            if (current >= 0) config.Bindings[current] = binding;
            else config.Bindings.Add(binding);
            Write(config);
            return binding;
        });
    }

    public bool Unbind(long projectId)
    {
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        return WithLock(() =>
        {
            var config = ReadOrCreate();
            var removed = config.Bindings.RemoveAll(binding => binding.ProjectId == projectId) != 0;
            if (removed) Write(config);
            return removed;
        });
    }

    public ProjectBinding SetAiReadConsent(long projectId, bool consent)
    {
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        return WithLock(() =>
        {
            var config = ReadOrCreate();
            var index = config.Bindings.FindIndex(binding => binding.ProjectId == projectId);
            if (index < 0) throw new KeyNotFoundException("Project is not bound.");
            if (consent && !Directory.Exists(config.Bindings[index].LocalPath))
                throw new DirectoryNotFoundException("Bound project directory is unavailable.");
            var binding = config.Bindings[index] with { AiReadConsent = consent };
            config.Bindings[index] = binding;
            Write(config);
            return binding;
        });
    }

    public IReadOnlyList<ProjectBinding> ListBindings() => WithLock(() => ReadOrCreate().Bindings.ToArray());

    public ProjectBinding? ResolveByProjectId(long projectId)
    {
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        return WithLock(() => ReadOrCreate().Bindings.Find(binding => binding.ProjectId == projectId));
    }

    public ProjectBinding? ResolveByPath(string localPath)
    {
        var normalized = NormalizePath(localPath);
        return WithLock(() => ReadOrCreate().Bindings.Find(binding => PathComparer.Equals(binding.LocalPath, normalized)));
    }

    private static string NormalizePath(string localPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        if (!Path.IsPathFullyQualified(localPath)) throw new ArgumentException("Project path must be absolute.", nameof(localPath));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(localPath));
    }

    private T WithLock<T>(Func<T> action)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory))
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("Configuration directory must be accessible only by its owner.");

        // All readers and writers take the same cross-process lock to avoid lost updates.
        var lockPath = path + ".lock";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var handle = new FileStream(lockPath, StreamOptions(FileMode.OpenOrCreate));
                if (!OperatingSystem.IsWindows()) CheckPermissions(lockPath);
                return action();
            }
            catch (IOException) when (attempt < 100)
            {
                Thread.Sleep(50);
            }
        }
    }

    private ConfigFile ReadOrCreate()
    {
        if (!File.Exists(path))
        {
            var created = new ConfigFile { Version = 1, InstallationId = Guid.NewGuid(), DisplayName = "BoyScout", Backend = StorageBackend.Sqlite };
            Write(created);
            return created;
        }
        if (!OperatingSystem.IsWindows()) CheckPermissions(path);
        try
        {
            var config = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException("Configuration is invalid.");
            if (config.Version != 1 || config.InstallationId == Guid.Empty || !Enum.IsDefined(config.Backend)
                || string.IsNullOrWhiteSpace(config.DisplayName) || config.DisplayName.Length > 100
                || config.DisplayName.Any(char.IsControl)
                || (!HasConnection(config) && (config.DatabaseUrl is not null || config.ProjectId is not null
                    || config.ClientEmail is not null || config.PrivateKey is not null || config.ProtectedPrivateKey is not null))
                || (config.Backend == StorageBackend.Firebase && !HasConnection(config))
                || (HasConnection(config) && !ValidConnection(config))
                || config.Bindings is null || config.Bindings.Any(binding => binding is null || binding.ProjectId <= 0
                    || string.IsNullOrWhiteSpace(binding.LocalPath) || binding.LocalPath != NormalizePath(binding.LocalPath))
                || config.Bindings.Select(binding => binding.ProjectId).Distinct().Count() != config.Bindings.Count
                || config.Bindings.Select(binding => binding.LocalPath).Distinct(PathComparer).Count() != config.Bindings.Count)
                throw new InvalidOperationException("Configuration is invalid.");
            return config;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException("Configuration is invalid or cannot be decrypted.");
        }
    }

    private static bool ValidConnection(ConfigFile config)
    {
        try { _ = Connection(config); return true; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException) { return false; }
    }

    private static FirebaseConnection Connection(ConfigFile config)
    {
        var key = OperatingSystem.IsWindows()
            ? Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(config.ProtectedPrivateKey!), null, DataProtectionScope.CurrentUser))
            : config.PrivateKey!;
        return new FirebaseConnection(config.DatabaseUrl!, config.ProjectId!, config.ClientEmail!, key);
    }

    private static bool HasConnection(ConfigFile config) =>
        !string.IsNullOrWhiteSpace(config.DatabaseUrl) && !string.IsNullOrWhiteSpace(config.ProjectId)
        && !string.IsNullOrWhiteSpace(config.ClientEmail)
        && (OperatingSystem.IsWindows() ? !string.IsNullOrWhiteSpace(config.ProtectedPrivateKey) && config.PrivateKey is null
            : !string.IsNullOrWhiteSpace(config.PrivateKey) && config.ProtectedPrivateKey is null);

    private static BoyScoutConfigStatus Status(ConfigFile config) =>
        new(config.InstallationId, config.DisplayName, config.Backend, HasConnection(config));

    private void Write(ConfigFile config)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, StreamOptions(FileMode.CreateNew)))
            {
                JsonSerializer.Serialize(stream, config, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void CheckPermissions(string file)
    {
        if (OperatingSystem.IsWindows()) return;
        if ((File.GetUnixFileMode(file) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("Configuration file must be accessible only by its owner.");
    }

    private static FileStreamOptions StreamOptions(FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    private sealed class ConfigFile
    {
        public required int Version { get; set; }
        public required Guid InstallationId { get; set; }
        public required string DisplayName { get; set; }
        public required StorageBackend Backend { get; set; }
        public string? DatabaseUrl { get; set; }
        public string? ProjectId { get; set; }
        public string? ClientEmail { get; set; }
        public string? PrivateKey { get; set; }
        public string? ProtectedPrivateKey { get; set; }
        public List<ProjectBinding> Bindings { get; set; } = [];
    }
}
