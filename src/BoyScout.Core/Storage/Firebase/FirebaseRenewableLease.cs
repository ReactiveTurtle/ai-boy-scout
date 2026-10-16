using System.Globalization;
using System.Text.Json;

namespace BoyScout.Core.Storage.Firebase;

public sealed record FirebaseLeaseHandle(string OwnerInstallationId, string OwnerName, string Token,
    long Generation, DateTimeOffset ExpiresAt);

/// <summary>
/// Coordinates ownership of one RTDB node. VerifyFencingAsync must be checked before committing a result;
/// a lease check cannot atomically fence a write to a separate result node.
/// </summary>
public sealed class FirebaseRenewableLease
{
    private const int MaxAttempts = 5;
    private readonly FirebaseRealtimeDatabaseTransport transport;
    private readonly string path;
    private readonly TimeSpan duration;

    public FirebaseRenewableLease(FirebaseRealtimeDatabaseTransport transport, string path, TimeSpan duration)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (string.IsNullOrWhiteSpace(path) || path.Split('/').Any(segment =>
                segment.Length == 0 || segment.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')))
            throw new ArgumentException("Invalid Firebase lease path.", nameof(path));
        if (duration <= TimeSpan.FromSeconds(1) || duration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(duration), "Lease duration must be greater than one second and at most one hour.");
        this.path = path;
        this.duration = duration;
    }

    public async Task<FirebaseLeaseHandle?> AcquireAsync(string ownerInstallationId, string ownerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerInstallationId)) throw new ArgumentException("Owner installation ID is required.", nameof(ownerInstallationId));
        if (string.IsNullOrWhiteSpace(ownerName)) throw new ArgumentException("Owner name is required.", nameof(ownerName));

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var now = ServerTime(snapshot);
            var previous = Parse(snapshot.Json);
            if (previous is { Token: not null } && previous.ExpiresAt > now) return null;
            if (previous?.Generation == long.MaxValue) throw new InvalidOperationException("Lease generation exhausted.");

            var handle = new FirebaseLeaseHandle(ownerInstallationId, ownerName, Guid.NewGuid().ToString("N"),
                (previous?.Generation ?? 0) + 1, now.Add(duration));
            if (await transport.PutIfMatchAsync(path, JsonSerializer.Serialize(handle), snapshot.ETag, cancellationToken))
                return handle;
        }
        throw new InvalidOperationException("Lease remained contended.");
    }

    public async Task<FirebaseLeaseHandle?> RenewAsync(FirebaseLeaseHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var now = ServerTime(snapshot);
            var current = Parse(snapshot.Json);
            if (!Matches(current, handle, now)) return null;

            var renewed = handle with { ExpiresAt = now.Add(duration) };
            if (await transport.PutIfMatchAsync(path, JsonSerializer.Serialize(renewed), snapshot.ETag, cancellationToken))
                return renewed;
        }
        throw new InvalidOperationException("Lease remained contended.");
    }

    public async Task ForceReleaseAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var now = ServerTime(snapshot);
            var current = Parse(snapshot.Json);
            if (current?.Generation == long.MaxValue) throw new InvalidOperationException("Lease generation exhausted.");
            var tombstone = new LeaseState(null, null, null, (current?.Generation ?? 0) + 1, now);
            if (await transport.PutIfMatchAsync(path, JsonSerializer.Serialize(tombstone), snapshot.ETag, cancellationToken))
                return;
        }
        throw new InvalidOperationException("Lease remained contended.");
    }

    /// <summary>Checks the current lease before a result commit; this is not atomic with a separate result write.</summary>
    public async Task<bool> VerifyFencingAsync(FirebaseLeaseHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var snapshot = await transport.GetAsync(path, cancellationToken);
        return Matches(Parse(snapshot.Json), handle, ServerTime(snapshot));
    }

    private static bool Matches(LeaseState? state, FirebaseLeaseHandle handle, DateTimeOffset now) =>
        state is not null && state.Token is not null && state.ExpiresAt > now &&
        state.Generation == handle.Generation && state.Token == handle.Token &&
        state.OwnerInstallationId == handle.OwnerInstallationId && state.OwnerName == handle.OwnerName &&
        state.ExpiresAt == handle.ExpiresAt;

    private static DateTimeOffset ServerTime(FirebaseSnapshot snapshot) =>
        snapshot.ServerDate is { } date && date >= DateTimeOffset.UnixEpoch && date.Offset == TimeSpan.Zero
            ? date
            : throw new InvalidOperationException("Firebase GET returned no valid UTC server Date; lease operation refused.");

    private static LeaseState? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return null;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Generation", out var generation) || generation.ValueKind != JsonValueKind.Number ||
            !generation.TryGetInt64(out var number) || number < 1 ||
            !root.TryGetProperty("ExpiresAt", out var expiry) || expiry.ValueKind != JsonValueKind.String ||
            !(expiry.GetString()!.EndsWith("Z", StringComparison.Ordinal) ||
              expiry.GetString()!.EndsWith("+00:00", StringComparison.Ordinal)) ||
            !DateTimeOffset.TryParse(expiry.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiresAt) || expiresAt.Offset != TimeSpan.Zero ||
            expiresAt < DateTimeOffset.UnixEpoch ||
            !root.TryGetProperty("Token", out var token) ||
            !root.TryGetProperty("OwnerInstallationId", out var installation) ||
            !root.TryGetProperty("OwnerName", out var name))
            throw new InvalidOperationException("Invalid Firebase lease state.");

        if (token.ValueKind == JsonValueKind.Null && installation.ValueKind == JsonValueKind.Null && name.ValueKind == JsonValueKind.Null)
            return new LeaseState(null, null, null, number, expiresAt);
        if (token.ValueKind != JsonValueKind.String || !Guid.TryParseExact(token.GetString(), "N", out _) ||
            installation.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(installation.GetString()) ||
            name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
            throw new InvalidOperationException("Invalid Firebase lease owner.");
        return new LeaseState(installation.GetString(), name.GetString(), token.GetString(), number, expiresAt);
    }

    private sealed record LeaseState(string? OwnerInstallationId, string? OwnerName, string? Token,
        long Generation, DateTimeOffset ExpiresAt);
}
