using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoyScout.Core.Reports;

namespace BoyScout.Core.Storage.Firebase;

public sealed record FirebaseReportProject(long Id, string Name);

public sealed record FirebaseReportNote(
    string Id, long ProjectId, string OriginalPath, string Title, string? Location,
    string Observation, string? Impact, string? SuggestedAction, string? DiscoveredDuring,
    string Status, string CreatedAt, string UpdatedAt, Guid SourceInstallationId, string SourceName,
    Guid? OriginInstallationId = null, string? OriginNoteId = null);

public sealed record FirebaseNoteImportResult(FirebaseReportNote Note, bool AlreadyImported);

public sealed record FirebaseNotePage(IReadOnlyList<FirebaseReportNote> Items, string? NextCursor);

// This repository is intentionally separate from IReportStore and is not wired into the application.
public sealed class FirebaseReportRepository(FirebaseRealtimeDatabaseTransport transport, FirebaseSequenceAllocator sequences)
{
    private const int MaxAttempts = 5;
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<FirebaseReportProject?> GetProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        var json = (await transport.GetAsync(ProjectPath(projectId) + "/Project", cancellationToken)).Json;
        var project = JsonSerializer.Deserialize<FirebaseReportProject>(json);
        if (project is not null && (project.Id != projectId || string.IsNullOrWhiteSpace(project.Name)))
            throw new InvalidOperationException("Remote project metadata is invalid.");
        return project;
    }

    public async Task<bool> VerifyProjectWriteAccessAsync(long projectId, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            if (snapshot.Json.Trim() == "null") return false;
            if (await transport.PutIfMatchAsync(path, snapshot.Json, snapshot.ETag, cancellationToken)) return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<FirebaseReportProject>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        var json = await transport.GetShallowAsync("report-projects", cancellationToken);
        using var root = JsonDocument.Parse(json);
        if (root.RootElement.ValueKind == JsonValueKind.Null) return Array.Empty<FirebaseReportProject>();
        if (root.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Remote project keys are invalid.");

        var ids = new List<long>();
        foreach (var entry in root.RootElement.EnumerateObject())
        {
            if (!long.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
                entry.Name != id.ToString(CultureInfo.InvariantCulture) || entry.Value.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("Remote project keys are invalid.");
            ids.Add(id);
        }

        ids.Sort();
        var projects = new FirebaseReportProject[ids.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, ids.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (index, token) =>
            {
                projects[index] = await GetProjectAsync(ids[index], token)
                    ?? throw new InvalidOperationException("Listed remote project metadata is missing.");
            });
        return projects;
    }

    public async Task<FirebaseReportProject> CreateProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(sequences);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Project name is required.", nameof(name));

        var id = await sequences.AllocateAsync("report-projects", cancellationToken);
        var project = new FirebaseReportProject(id, name.Trim());
        var path = ProjectPath(id);
        var snapshot = await transport.GetAsync(path, cancellationToken);
        if (snapshot.Json.Trim() != "null")
            throw new InvalidOperationException("Allocated project ID already exists; refusing to overwrite it.");
        if (!await transport.PutIfMatchAsync(path, JsonSerializer.Serialize(new ProjectDocument(project, new())), snapshot.ETag, cancellationToken))
            throw new InvalidOperationException("Project ID was created concurrently; refusing to overwrite it.");
        return project;
    }

    public async Task<FirebaseReportNote> AddNoteAsync(long projectId, CreateReport request,
        Guid sourceInstallationId, string sourceName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        if (string.IsNullOrWhiteSpace(request.Project)) throw new ArgumentException("Original path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Title is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Observation)) throw new ArgumentException("Observation is required.", nameof(request));
        if (request.Location is not null && string.IsNullOrWhiteSpace(request.Location) ||
            request.Impact is not null && string.IsNullOrWhiteSpace(request.Impact) ||
            request.SuggestedAction is not null && string.IsNullOrWhiteSpace(request.SuggestedAction) ||
            request.DiscoveredDuring is not null && string.IsNullOrWhiteSpace(request.DiscoveredDuring))
            throw new ArgumentException("Optional note fields cannot be empty when provided.", nameof(request));
        if (sourceInstallationId == Guid.Empty) throw new ArgumentException("Installation ID is required.", nameof(sourceInstallationId));
        if (string.IsNullOrWhiteSpace(sourceName)) throw new ArgumentException("Source name is required.", nameof(sourceName));

        var sequence = await sequences.AllocateAsync("report-notes", cancellationToken);
        var id = $"BSR-{projectId.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString("D19", CultureInfo.InvariantCulture)}";
        var timestamp = UtcTimestamp();
        var note = new FirebaseReportNote(id, projectId, request.Project, request.Title, request.Location,
            request.Observation, request.Impact, request.SuggestedAction, request.DiscoveredDuring,
            ReportStatuses.Open, timestamp, timestamp, sourceInstallationId, sourceName.Trim());
        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var document = ReadProject(snapshot.Json, projectId);
            if (document is null) throw new InvalidOperationException("Remote project does not exist.");
            if (!document.Notes.TryAdd(id, note))
                throw new InvalidOperationException("Note ID already exists; refusing to overwrite it.");
            if (await transport.PutIfMatchAsync(path, JsonSerializer.Serialize(document, JsonOptions), snapshot.ETag, cancellationToken))
                return note;
        }
        throw new InvalidOperationException("Project remained contended; note was not written.");
    }

    public async Task<FirebaseNoteImportResult> ImportNoteAsync(long projectId, BoyScoutReport source,
        Guid originInstallationId, string sourceName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        if (originInstallationId == Guid.Empty) throw new ArgumentException("Origin installation ID is required.", nameof(originInstallationId));
        if (string.IsNullOrWhiteSpace(source.Id)) throw new ArgumentException("Origin note ID is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(source.Project) || string.IsNullOrWhiteSpace(source.Title) ||
            string.IsNullOrWhiteSpace(source.Observation) || !ReportStatuses.IsValid(source.Status))
            throw new ArgumentException("Local note data is invalid.", nameof(source));
        if (string.IsNullOrWhiteSpace(sourceName)) throw new ArgumentException("Source name is required.", nameof(sourceName));

        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var document = ReadProject(snapshot.Json, projectId);
            if (document is null) throw new InvalidOperationException("Remote project does not exist.");

            var imported = document.Notes.Values.FirstOrDefault(note =>
                note.OriginInstallationId == originInstallationId && note.OriginNoteId == source.Id);
            if (imported is not null) return new FirebaseNoteImportResult(imported, AlreadyImported: true);

            var sequence = await sequences.AllocateAsync("report-notes", cancellationToken);
            var id = $"BSR-{projectId.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString("D19", CultureInfo.InvariantCulture)}";
            var note = new FirebaseReportNote(id, projectId, source.Project, source.Title, source.Location,
                source.Observation, source.Impact, source.SuggestedAction, source.DiscoveredDuring,
                source.Status, source.CreatedAt, source.UpdatedAt,
                source.SourceInstallationId ?? originInstallationId,
                string.IsNullOrWhiteSpace(source.SourceName) ? sourceName.Trim() : source.SourceName,
                originInstallationId, source.Id);
            if (!document.Notes.TryAdd(id, note))
                throw new InvalidOperationException("Allocated note ID already exists; refusing to overwrite it.");
            if (await transport.PutIfMatchAsync(path, JsonSerializer.Serialize(document, JsonOptions), snapshot.ETag, cancellationToken))
                return new FirebaseNoteImportResult(note, AlreadyImported: false);
        }

        throw new InvalidOperationException("Project remained contended; note was not imported.");
    }

    public async Task<FirebaseReportNote?> GetNoteAsync(string id, CancellationToken cancellationToken = default)
    {
        var projectId = ParseProjectId(id);
        var json = (await transport.GetAsync(ProjectPath(projectId) + "/Notes/" + id, cancellationToken)).Json;
        var note = JsonSerializer.Deserialize<FirebaseReportNote>(json);
        if (note is not null && (note.Id != id || note.ProjectId != projectId || !ReportStatuses.IsValid(note.Status)))
            throw new InvalidOperationException("Remote note data is invalid.");
        return note;
    }

    public async Task<FirebaseNotePage> BrowseNotesAsync(long projectId, string? before, int limit,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (before is not null && ParseProjectId(before) != projectId)
            throw new ArgumentException("Cursor belongs to a different project.", nameof(before));

        var json = await transport.GetKeyPageAsync(ProjectPath(projectId) + "/Notes", before,
            limit + (before is null ? 1 : 2), cancellationToken);
        var notes = JsonSerializer.Deserialize<Dictionary<string, FirebaseReportNote>>(json) ?? new();
        if (notes.Any(entry => entry.Value is null || entry.Key != entry.Value.Id ||
            entry.Value.ProjectId != projectId || !ReportStatuses.IsValid(entry.Value.Status)))
            throw new InvalidOperationException("Remote note data is invalid.");

        var descending = notes.OrderByDescending(entry => entry.Key, StringComparer.Ordinal)
            .Where(entry => before is null || string.CompareOrdinal(entry.Key, before) < 0)
            .Select(entry => entry.Value).ToList();
        var hasMore = descending.Count > limit;
        var items = descending.Take(limit).ToArray();
        return new FirebaseNotePage(items, hasMore ? items[^1].Id : null);
    }

    public async Task<FirebaseReportNote?> UpdateNoteStatusAsync(string id, string status,
        CancellationToken cancellationToken = default)
    {
        if (!ReportStatuses.IsValid(status)) throw new ArgumentException($"Unsupported report status: {status}.", nameof(status));
        var projectId = ParseProjectId(id);
        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var document = ReadProject(snapshot.Json, projectId);
            if (document is null || !document.Notes.TryGetValue(id, out var note)) return null;
            var updated = note with { Status = status, UpdatedAt = UtcTimestamp() };
            document.Notes[id] = updated;
            if (await transport.PutIfMatchAsync(path, JsonSerializer.Serialize(document, JsonOptions), snapshot.ETag, cancellationToken))
                return updated;
        }
        throw new InvalidOperationException("Project remained contended; status was not updated.");
    }

    private static ProjectDocument? ReadProject(string json, long projectId)
    {
        var document = JsonSerializer.Deserialize<ProjectDocument>(json);
        if (document is not null && (document.Project is null || document.Project.Id != projectId ||
            string.IsNullOrWhiteSpace(document.Project.Name) || document.Notes is null ||
            document.Notes.Any(entry => entry.Value is null || entry.Key != entry.Value.Id ||
                entry.Value.ProjectId != projectId || !ReportStatuses.IsValid(entry.Value.Status))))
            throw new InvalidOperationException("Remote project data is invalid; refusing to overwrite it.");
        return document;
    }

    private static long ParseProjectId(string id)
    {
        if (id is null || !id.StartsWith("BSR-", StringComparison.Ordinal))
            throw new ArgumentException("Invalid remote note ID.", nameof(id));
        var parts = id.Split('-');
        if (parts.Length != 3 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var projectId) ||
            projectId <= 0 || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0 ||
            id != $"BSR-{projectId.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString("D19", CultureInfo.InvariantCulture)}")
            throw new ArgumentException("Invalid remote note ID.", nameof(id));
        return projectId;
    }

    private static string ProjectPath(long id) => "report-projects/" + id.ToString(CultureInfo.InvariantCulture);
    private static string UtcTimestamp() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private sealed record ProjectDocument(FirebaseReportProject Project, Dictionary<string, FirebaseReportNote> Notes)
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
    }
}
