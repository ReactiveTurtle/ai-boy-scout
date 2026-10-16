using System.Net;
using System.Text.Json;
using BoyScout.Core.Reports;
using BoyScout.Core.Storage.Firebase;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class FirebaseReportRepositoryTests
{
    private static readonly Guid Installation = Guid.Parse("c0b25db3-6f62-4636-ae8a-e34a0c90871f");
    private static CreateReport Request(string path, string title = "Finding") =>
        new(path, title, null, "Observed", null, null, null);

    [Fact]
    public async Task Creates_independent_projects_and_keeps_original_path_only_on_notes()
    {
        using var server = new Server();
        var first = Repository(server);
        var second = Repository(server);
        var a = await first.CreateProjectAsync(" Alpha ", TestContext.Current.CancellationToken);
        var b = await second.CreateProjectAsync("Beta", TestContext.Current.CancellationToken);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal("Alpha", a.Name);

        var note = await second.AddNoteAsync(b.Id, Request("C:/old/location"), Installation, " laptop ", TestContext.Current.CancellationToken);
        Assert.Equal("C:/old/location", note.OriginalPath);
        Assert.Equal("laptop", note.SourceName);
        Assert.Equal(Installation, note.SourceInstallationId);
        Assert.Equal(ReportStatuses.Open, note.Status);
        Assert.Equal(note, await first.GetNoteAsync(note.Id, TestContext.Current.CancellationToken));
        Assert.Null(await first.GetNoteAsync($"BSR-{a.Id}-{999L:D19}", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("C:/old/location", server.Read($"report-projects/{a.Id}"));
        Assert.Contains("Beta", server.Read($"report-projects/{b.Id}"));
    }

    [Fact]
    public async Task Two_instances_retry_conflicts_without_losing_notes_or_status()
    {
        using var server = new Server();
        var first = Repository(server);
        var second = Repository(server);
        var project = await first.CreateProjectAsync("Shared", TestContext.Current.CancellationToken);
        server.ConflictNextProjectPut = true;
        var notes = await Task.WhenAll(
            Task.Run(() => first.AddNoteAsync(project.Id, Request("/one", "One"), Installation, "one", TestContext.Current.CancellationToken)),
            Task.Run(() => second.AddNoteAsync(project.Id, Request("/two", "Two"), Installation, "two", TestContext.Current.CancellationToken)));
        Assert.NotEqual(notes[0].Id, notes[1].Id);
        Assert.True(server.ProjectConflicts >= 1);
        foreach (var note in notes) Assert.Equal(note, await second.GetNoteAsync(note.Id, TestContext.Current.CancellationToken));

        server.ConflictNextProjectPut = true;
        var changed = await first.UpdateNoteStatusAsync(notes[0].Id, ReportStatuses.Resolved, TestContext.Current.CancellationToken);
        Assert.Equal(ReportStatuses.Resolved, changed!.Status);
        Assert.Equal(ReportStatuses.Resolved, (await second.GetNoteAsync(notes[0].Id, TestContext.Current.CancellationToken))!.Status);
        Assert.Equal(ReportStatuses.Open, (await first.GetNoteAsync(notes[1].Id, TestContext.Current.CancellationToken))!.Status);
    }

    [Fact]
    public async Task Note_mutations_preserve_other_project_workflow_data()
    {
        using var server = new Server();
        var repository = Repository(server);
        var project = await repository.CreateProjectAsync("Shared", TestContext.Current.CancellationToken);
        var path = $"report-projects/{project.Id}";
        var original = server.Read(path);
        server.Set(path, original[..^1] + ",\"Tasks\":{\"BST-1\":{\"state\":\"todo\"}}}");

        var note = await repository.AddNoteAsync(project.Id, Request("/local"), Installation, "source", TestContext.Current.CancellationToken);
        await repository.UpdateNoteStatusAsync(note.Id, ReportStatuses.Planned, TestContext.Current.CancellationToken);

        using var updated = JsonDocument.Parse(server.Read(path));
        Assert.Equal("todo", updated.RootElement.GetProperty("Tasks").GetProperty("BST-1").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Collisions_and_missing_project_never_overwrite_data()
    {
        using var server = new Server();
        var repository = Repository(server);
        var project = await repository.CreateProjectAsync("Original", TestContext.Current.CancellationToken);
        var original = server.Read($"report-projects/{project.Id}");
        server.Set("counters/report-projects", "0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateProjectAsync("Intruder", TestContext.Current.CancellationToken));
        Assert.Equal(original, server.Read($"report-projects/{project.Id}"));

        var note = await repository.AddNoteAsync(project.Id, Request("/path"), Installation, "source", TestContext.Current.CancellationToken);
        var withNote = server.Read($"report-projects/{project.Id}");
        server.Set("counters/report-notes", "0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddNoteAsync(project.Id, Request("/other"), Installation, "source", TestContext.Current.CancellationToken));
        Assert.Equal(withNote, server.Read($"report-projects/{project.Id}"));
        Assert.Equal(note, await repository.GetNoteAsync(note.Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddNoteAsync(987, Request("/path"), Installation, "source", TestContext.Current.CancellationToken));
        Assert.Null(await repository.UpdateNoteStatusAsync($"BSR-987-{1L:D19}", ReportStatuses.Resolved, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Project_creation_conflict_does_not_replace_concurrent_project()
    {
        using var server = new Server();
        var repository = Repository(server);
        server.ConflictNextProjectPut = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateProjectAsync("New", TestContext.Current.CancellationToken));
        Assert.Equal("null", server.Read("report-projects/1"));
        Assert.Equal(1, server.ProjectWrites);
        Assert.Equal(1, server.ProjectConflicts);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("OPEN")]
    [InlineData("")]
    public async Task Invalid_status_and_ambiguous_put_do_not_retry(string invalidStatus)
    {
        using var server = new Server();
        var repository = Repository(server);
        var project = await repository.CreateProjectAsync("Project", TestContext.Current.CancellationToken);
        var note = await repository.AddNoteAsync(project.Id, Request("/path"), Installation, "source", TestContext.Current.CancellationToken);
        var before = server.Read($"report-projects/{project.Id}");
        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpdateNoteStatusAsync(note.Id, invalidStatus, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetNoteAsync("BSR-1-../2", TestContext.Current.CancellationToken));
        Assert.Equal(before, server.Read($"report-projects/{project.Id}"));

        server.ThrowNextProjectPut = true;
        var writes = server.ProjectWrites;
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.UpdateNoteStatusAsync(note.Id, ReportStatuses.Planned, TestContext.Current.CancellationToken));
        Assert.Equal(writes + 1, server.ProjectWrites);
        Assert.Equal(before, server.Read($"report-projects/{project.Id}"));
    }

    [Fact]
    public async Task Browse_reads_only_bounded_project_notes_in_descending_key_order()
    {
        using var server = new Server();
        var repository = Repository(server);
        var project = await repository.CreateProjectAsync("Paged", TestContext.Current.CancellationToken);
        var other = await repository.CreateProjectAsync("Other", TestContext.Current.CancellationToken);
        await repository.AddNoteAsync(other.Id, Request("/other"), Installation, "other", TestContext.Current.CancellationToken);
        var notes = new List<FirebaseReportNote>();
        for (var i = 0; i < 12; i++)
            notes.Add(await repository.AddNoteAsync(project.Id, Request($"/note-{i}"), Installation, "source", TestContext.Current.CancellationToken));

        server.QueryRequests.Clear();
        var first = await repository.BrowseNotesAsync(project.Id, null, 10, TestContext.Current.CancellationToken);
        Assert.Equal(notes.AsEnumerable().Reverse().Take(10), first.Items);
        Assert.Equal(first.Items[^1].Id, first.NextCursor);
        var second = await repository.BrowseNotesAsync(project.Id, first.NextCursor, 10, TestContext.Current.CancellationToken);
        Assert.Equal(notes.Take(2).Reverse(), second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(new[] { (project.Id, (string?)null, 11), (project.Id, first.NextCursor, 12) }, server.QueryRequests);
    }

    [Fact]
    public async Task Browse_cursor_excludes_newer_concurrent_insertions()
    {
        using var server = new Server();
        var repository = Repository(server);
        var project = await repository.CreateProjectAsync("Paged", TestContext.Current.CancellationToken);
        var notes = new List<FirebaseReportNote>();
        for (var i = 0; i < 5; i++)
            notes.Add(await repository.AddNoteAsync(project.Id, Request($"/note-{i}"), Installation, "source", TestContext.Current.CancellationToken));

        var first = await repository.BrowseNotesAsync(project.Id, null, 2, TestContext.Current.CancellationToken);
        var inserted = await repository.AddNoteAsync(project.Id, Request("/new"), Installation, "source", TestContext.Current.CancellationToken);
        var second = await repository.BrowseNotesAsync(project.Id, first.NextCursor, 2, TestContext.Current.CancellationToken);
        var third = await repository.BrowseNotesAsync(project.Id, second.NextCursor, 2, TestContext.Current.CancellationToken);
        Assert.Equal(notes.AsEnumerable().Reverse(), first.Items.Concat(second.Items).Concat(third.Items));
        Assert.DoesNotContain(inserted, second.Items.Concat(third.Items));
        Assert.Null(third.NextCursor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Browse_rejects_invalid_limits_before_http(int limit)
    {
        using var server = new Server();
        var repository = Repository(server);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.BrowseNotesAsync(1, null, limit, TestContext.Current.CancellationToken));
        Assert.Empty(server.QueryRequests);
    }

    [Fact]
    public async Task Browse_rejects_cross_project_and_invalid_cursors()
    {
        using var server = new Server();
        var repository = Repository(server);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.BrowseNotesAsync(1, $"BSR-2-{1L:D19}", 10, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.BrowseNotesAsync(1, "BSR-1-1", 10, TestContext.Current.CancellationToken));
        Assert.Empty(server.QueryRequests);
    }

    private static FirebaseReportRepository Repository(Server server)
    {
        var transport = new FirebaseRealtimeDatabaseTransport(
            new FirebaseConnection("https://example.firebaseio.com/", "project", "service@example.com", "test-key"),
            server.Client, _ => Task.FromResult("token"));
        return new FirebaseReportRepository(transport, new FirebaseSequenceAllocator(transport));
    }

    private sealed class Server : IDisposable
    {
        private readonly object gate = new();
        private readonly Dictionary<string, (string Json, int Version)> nodes = new();
        public HttpClient Client { get; }
        public bool ConflictNextProjectPut { get; set; }
        public bool ThrowNextProjectPut { get; set; }
        public int ProjectConflicts { get; private set; }
        public int ProjectWrites { get; private set; }
        public List<(long ProjectId, string? Before, int Limit)> QueryRequests { get; } = new();

        public Server() => Client = new HttpClient(new Handler(Respond));

        public string Read(string path)
        {
            lock (gate) return nodes.TryGetValue(path, out var node) ? node.Json : "null";
        }

        public void Set(string path, string json)
        {
            lock (gate)
            {
                var version = nodes.TryGetValue(path, out var previous) ? previous.Version : 0;
                nodes[path] = (json, version + 1);
            }
        }

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            lock (gate)
            {
                var path = request.RequestUri!.AbsolutePath.TrimStart('/');
                Assert.EndsWith(".json", path);
                path = path[..^5];
                if (request.RequestUri.Query.Length > 0)
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.False(request.Headers.Contains("X-Firebase-ETag"));
                    var parameters = request.RequestUri.Query.TrimStart('?').Split('&')
                        .Select(part => part.Split('=', 2))
                        .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
                    Assert.Equal("\"$key\"", parameters["orderBy"]);
                     Assert.InRange(int.Parse(parameters["limitToLast"]), 1, 102);
                    Assert.Equal(parameters.ContainsKey("endAt") ? 3 : 2, parameters.Count);
                    Assert.EndsWith("/Notes", path);
                    var projectId = long.Parse(path.Split('/')[1]);
                    var before = parameters.TryGetValue("endAt", out var endAt)
                        ? JsonSerializer.Deserialize<string>(endAt) : null;
                    var limit = int.Parse(parameters["limitToLast"]);
                    QueryRequests.Add((projectId, before, limit));
                    using var project = JsonDocument.Parse(Read($"report-projects/{projectId}"));
                    var entries = project.RootElement.GetProperty("Notes").EnumerateObject()
                        .Where(entry => before is null || string.CompareOrdinal(entry.Name, before) <= 0)
                        .OrderBy(entry => entry.Name, StringComparer.Ordinal).TakeLast(limit)
                        .ToDictionary(entry => entry.Name, entry => entry.Value.Clone());
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(entries)) };
                }
                if (request.Method == HttpMethod.Get)
                    Assert.Equal("true", Assert.Single(request.Headers.GetValues("X-Firebase-ETag")));
                if (request.Method == HttpMethod.Get && path.Contains("/Notes/", StringComparison.Ordinal))
                {
                    var parts = path.Split('/');
                    using var project = JsonDocument.Parse(Read($"report-projects/{parts[1]}"));
                    var note = project.RootElement.GetProperty("Notes").TryGetProperty(parts[3], out var value)
                        ? value.GetRawText() : "null";
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(note) };
                    response.Headers.TryAddWithoutValidation("ETag", "\"v0\"");
                    return response;
                }
                var current = nodes.TryGetValue(path, out var stored) ? stored : ("null", 0);
                if (request.Method == HttpMethod.Get)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(current.Item1) };
                    response.Headers.TryAddWithoutValidation("ETag", $"\"v{current.Item2}\"");
                    return response;
                }
                Assert.Equal(HttpMethod.Put, request.Method);
                var projectWrite = path.StartsWith("report-projects/", StringComparison.Ordinal);
                if (projectWrite) ProjectWrites++;
                if (projectWrite && ThrowNextProjectPut)
                {
                    ThrowNextProjectPut = false;
                    throw new HttpRequestException("Ambiguous PUT");
                }
                if (projectWrite && ConflictNextProjectPut)
                {
                    ConflictNextProjectPut = false;
                    ProjectConflicts++;
                    nodes[path] = (current.Item1, current.Item2 + 1);
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }
                if (Assert.Single(request.Headers.GetValues("if-match")) != $"\"v{current.Item2}\"")
                {
                    if (projectWrite) ProjectConflicts++;
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var parsed = JsonDocument.Parse(json);
                nodes[path] = (json, current.Item2 + 1);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        public void Dispose() => Client.Dispose();

        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(respond(request));
        }
    }
}
