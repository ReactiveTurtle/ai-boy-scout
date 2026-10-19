using System.Globalization;
using System.Text.Json.Nodes;

namespace BoyScout.Core.Storage.Firebase;

public sealed record FirebaseProjectLeaseHandle(string ReportId, string OwnerInstallationId,
    string OwnerName, string Token, long Generation, DateTimeOffset ExpiresAt);

// The lease and its protected proposal live in the same project-root CAS domain.
public sealed class FirebaseProjectLease
{
    private const int MaxAttempts = 5;
    private readonly FirebaseRealtimeDatabaseTransport transport;
    private readonly TimeSpan duration;

    public FirebaseProjectLease(FirebaseRealtimeDatabaseTransport transport, TimeSpan duration)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (duration <= TimeSpan.FromSeconds(1) || duration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(duration));
        this.duration = duration;
    }

    public async Task<FirebaseProjectLeaseHandle?> AcquireAsync(string reportId, string ownerInstallationId,
        string ownerName, CancellationToken cancellationToken = default)
    {
        var path = Path(reportId);
        if (string.IsNullOrWhiteSpace(ownerInstallationId)) throw new ArgumentException("Installation ID is required.", nameof(ownerInstallationId));
        if (string.IsNullOrWhiteSpace(ownerName)) throw new ArgumentException("Owner name is required.", nameof(ownerName));
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var now = ServerTime(snapshot);
            var root = ReadRoot(snapshot.Json, reportId);
            var leases = Leases(root);
            var previous = ReadState(leases, reportId);
            if (previous is { Token: not null } && previous.ExpiresAt > now) return null;
            if (previous?.Generation == long.MaxValue) throw new InvalidOperationException("Lease generation exhausted.");
            var handle = new FirebaseProjectLeaseHandle(reportId, ownerInstallationId.Trim(), ownerName.Trim(),
                Guid.NewGuid().ToString("N"), (previous?.Generation ?? 0) + 1, now.Add(duration));
            leases[reportId] = WriteState(handle);
            root["ProcessingLeases"] = leases;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken)) return handle;
        }
        throw new InvalidOperationException("Project lease remained contended.");
    }

    public async Task<FirebaseProjectLeaseHandle?> RenewAsync(FirebaseProjectLeaseHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var path = Path(handle.ReportId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var now = ServerTime(snapshot);
            var root = ReadRoot(snapshot.Json, handle.ReportId);
            var leases = Leases(root);
            if (!Matches(ReadState(leases, handle.ReportId), handle, now)) return null;
            var renewed = handle with { ExpiresAt = now.Add(duration) };
            leases[handle.ReportId] = WriteState(renewed);
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken)) return renewed;
        }
        throw new InvalidOperationException("Project lease remained contended.");
    }

    public async Task ForceReleaseAsync(string reportId, CancellationToken cancellationToken = default)
    {
        var path = Path(reportId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var now = ServerTime(snapshot);
            var root = ReadRoot(snapshot.Json, reportId);
            var leases = Leases(root);
            var previous = ReadState(leases, reportId);
            if (previous?.Generation == long.MaxValue) throw new InvalidOperationException("Lease generation exhausted.");
            leases[reportId] = new JsonObject
            {
                ["OwnerInstallationId"] = null, ["OwnerName"] = null, ["Token"] = null,
                ["Generation"] = (previous?.Generation ?? 0) + 1, ["ExpiresAt"] = now.ToString("O", CultureInfo.InvariantCulture)
            };
            root["ProcessingLeases"] = leases;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken)) return;
        }
        throw new InvalidOperationException("Project lease remained contended.");
    }

    internal static bool IsValid(JsonObject root, FirebaseProjectLeaseHandle handle, DateTimeOffset now) =>
        Matches(ReadState(Leases(root), handle.ReportId), handle, now);

    internal static void Invalidate(JsonObject root, string reportId, DateTimeOffset now)
    {
        var leases = Leases(root);
        var previous = ReadState(leases, reportId);
        if (previous?.Generation == long.MaxValue) throw new InvalidOperationException("Lease generation exhausted.");
        leases[reportId] = new JsonObject
        {
            ["OwnerInstallationId"] = null, ["OwnerName"] = null, ["Token"] = null,
            ["Generation"] = (previous?.Generation ?? 0) + 1,
            ["ExpiresAt"] = now.ToString("O", CultureInfo.InvariantCulture)
        };
        root["ProcessingLeases"] = leases;
    }

    internal static DateTimeOffset ServerTime(FirebaseSnapshot snapshot) =>
        snapshot.ServerDate is { } date && date >= DateTimeOffset.UnixEpoch && date.Offset == TimeSpan.Zero
            ? date : throw new InvalidOperationException("Firebase GET returned no valid UTC server Date; lease operation refused.");

    private static bool Matches(FirebaseProjectLeaseHandle? state, FirebaseProjectLeaseHandle handle, DateTimeOffset now) =>
        state is not null && state.Token is not null && state.ExpiresAt > now &&
        state.ReportId == handle.ReportId && state.OwnerInstallationId == handle.OwnerInstallationId &&
        state.OwnerName == handle.OwnerName && state.Token == handle.Token &&
        state.Generation == handle.Generation && state.ExpiresAt == handle.ExpiresAt;

    private static JsonObject WriteState(FirebaseProjectLeaseHandle handle) => new()
    {
        ["OwnerInstallationId"] = handle.OwnerInstallationId, ["OwnerName"] = handle.OwnerName,
        ["Token"] = handle.Token, ["Generation"] = handle.Generation,
        ["ExpiresAt"] = handle.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
    };

    private static FirebaseProjectLeaseHandle? ReadState(JsonObject leases, string reportId)
    {
        if (!leases.TryGetPropertyValue(reportId, out var value)) return null;
        if (value is not JsonObject state || state["Generation"] is not JsonValue generation ||
            !generation.TryGetValue<long>(out var number) || number < 1 ||
            state["ExpiresAt"] is not JsonValue expiry || !expiry.TryGetValue<string>(out var text) ||
            !(text.EndsWith("Z", StringComparison.Ordinal) || text.EndsWith("+00:00", StringComparison.Ordinal)) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires) ||
            expires.Offset != TimeSpan.Zero || expires < DateTimeOffset.UnixEpoch)
            throw new InvalidOperationException("Invalid project lease state.");
        if (state["Token"] is null && state["OwnerInstallationId"] is null && state["OwnerName"] is null &&
            state.ContainsKey("Token") && state.ContainsKey("OwnerInstallationId") && state.ContainsKey("OwnerName"))
            return new FirebaseProjectLeaseHandle(reportId, null!, null!, null!, number, expires);
        if (state["Token"] is not JsonValue token || !token.TryGetValue<string>(out var tokenText) ||
            !Guid.TryParseExact(tokenText, "N", out _) ||
            state["OwnerInstallationId"] is not JsonValue owner || !owner.TryGetValue<string>(out var ownerText) ||
            string.IsNullOrWhiteSpace(ownerText) || state["OwnerName"] is not JsonValue name ||
            !name.TryGetValue<string>(out var nameText) || string.IsNullOrWhiteSpace(nameText))
            throw new InvalidOperationException("Invalid project lease owner.");
        return new FirebaseProjectLeaseHandle(reportId, ownerText, nameText, tokenText, number, expires);
    }

    private static JsonObject Leases(JsonObject root) => root["ProcessingLeases"] switch
    {
        null when !root.ContainsKey("ProcessingLeases") => new JsonObject(),
        JsonObject leases => leases,
        _ => throw new InvalidOperationException("Invalid project leases collection.")
    };

    private static string Path(string reportId)
    {
        if (reportId is null) throw new ArgumentException("Invalid remote note ID.", nameof(reportId));
        var parts = reportId.Split('-');
        if (parts.Length != 3 || parts[0] != "BSR" ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
            !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0 ||
            reportId != $"BSR-{id.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString("D19", CultureInfo.InvariantCulture)}")
            throw new ArgumentException("Invalid remote note ID.", nameof(reportId));
        return "report-projects/" + id.ToString(CultureInfo.InvariantCulture);
    }

    private static JsonObject ReadRoot(string json, string reportId)
    {
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { throw new InvalidOperationException("Invalid remote project data."); }
        var id = long.Parse(reportId.Split('-')[1], CultureInfo.InvariantCulture);
        if (parsed is not JsonObject root || root["Project"] is not JsonObject project ||
            project["Id"] is not JsonValue projectId || !projectId.TryGetValue<long>(out var number) || number != id ||
            root["Notes"] is not JsonObject notes || notes[reportId] is not JsonObject note ||
            note["Id"] is not JsonValue noteId || !noteId.TryGetValue<string>(out var text) || text != reportId ||
            note["ProjectId"] is not JsonValue noteProjectId ||
            !noteProjectId.TryGetValue<long>(out var noteNumber) || noteNumber != id)
            throw new InvalidOperationException("Invalid remote project data.");
        return root;
    }
}
