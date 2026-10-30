using System.Net;
using System.Text.Json;
using BoyScout.Application.Reports;
using BoyScout.Infrastructure.Reports;
using BoyScout.Infrastructure.Persistence;
using BoyScout.Infrastructure.Configuration;
using BoyScout.Infrastructure.Firebase;
using BoyScout.Domain.Reports;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class FirebaseReportStoreTests
{
    [Fact]
    public void Disabled_binding_keeps_add_in_sqlite()
    {
        using var fixture = new Fixture(firebaseEnabled: false);
        var report = fixture.Store.Add(Request(fixture.ProjectPath));
        Assert.StartsWith("BSR-", report.Id);
        Assert.Equal(1, fixture.LocalReportCount());
        Assert.Equal(0, fixture.Server.ProjectWrites);
    }

    [Fact]
    public void Enabled_add_writes_remote_only_and_preserves_attribution_and_local_project_id()
    {
        using var fixture = new Fixture();
        var report = fixture.Store.Add(Request(fixture.ProjectPath));
        Assert.Equal($"BSR-1-{1L:D19}", report.Id);
        Assert.Equal(fixture.ProjectPath, report.Project);
        Assert.Equal(fixture.Project.Id, report.ProjectId);
        Assert.Equal(fixture.Config.LoadStatus().DisplayName, report.SourceName);
        Assert.Equal(fixture.Config.LoadStatus().InstallationId, report.SourceInstallationId);
        Assert.Equal(0, fixture.LocalReportCount());
        Assert.Equal(1, fixture.Server.ProjectWrites);
    }

    [Fact]
    public void List_includes_remote_notes_filtered_by_bound_path_and_status()
    {
        using var fixture = new Fixture();
        fixture.Server.AddNote(1, "BSR-1-0000000000000000001", fixture.ProjectPath, "open", "Matching");
        fixture.Server.AddNote(1, "BSR-1-0000000000000000002", fixture.ProjectPath, "resolved", "Wrong status");
        var otherPath = Path.Combine(fixture.Root, "other");
        Directory.CreateDirectory(otherPath);
        fixture.AddEnabledBinding(2, otherPath);
        fixture.Server.AddNote(2, "BSR-2-0000000000000000003", otherPath, "open", "Wrong path");

        var report = Assert.Single(fixture.Store.List(fixture.ProjectPath, "open", 10));
        Assert.Equal("Matching", report.Title);
        Assert.Equal("BSR-1-0000000000000000001", report.Id);
        Assert.Equal(fixture.Project.Id, report.ProjectId);
        Assert.Equal("remote-source", report.SourceName);
    }

    [Fact]
    public void List_keeps_local_history_alongside_enabled_firebase_notes()
    {
        using var fixture = new Fixture();
        var local = new ReportStore(fixture.Database).Add(Request(fixture.ProjectPath));
        fixture.Server.AddNote(1, "BSR-1-0000000000000000001", fixture.ProjectPath, "open", "Firebase finding");

        var reports = fixture.Store.List(fixture.ProjectPath, null, 10);

        Assert.Equal(2, reports.Count);
        Assert.Contains(reports, report => report.Id == local.Id);
        Assert.Contains(reports, report => report.Id == "BSR-1-0000000000000000001");
        Assert.Equal(1, fixture.LocalReportCount());
    }

    [Fact]
    public void Get_and_update_status_accept_remote_ids()
    {
        using var fixture = new Fixture();
        const string id = "BSR-1-0000000000000000001";
        fixture.Server.AddNote(1, id, fixture.ProjectPath, "open", "Remote");
        Assert.Equal("Remote", fixture.Store.Get(id)!.Title);
        Assert.Equal(ReportStatuses.Resolved, fixture.Store.UpdateStatus(id, ReportStatuses.Resolved)!.Status);
        Assert.Equal(0, fixture.LocalReportCount());
    }

    [Theory]
    [InlineData("BSR-1-1")]
    [InlineData("BSR-9-0000000000000000001")]
    public void Invalid_or_unbound_remote_ids_are_rejected(string id)
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => fixture.Store.Get(id));
        Assert.Throws<ArgumentException>(() => fixture.Store.UpdateStatus(id, ReportStatuses.Resolved));
        Assert.Equal(0, fixture.LocalReportCount());
    }

    [Fact]
    public void Firebase_write_failure_does_not_fall_back_to_sqlite()
    {
        using var fixture = new Fixture();
        fixture.Server.FailNextProjectWrite = true;
        Assert.Throws<HttpRequestException>(() => fixture.Store.Add(Request(fixture.ProjectPath)));
        Assert.Equal(0, fixture.LocalReportCount());
    }

    private static CreateReport Request(string path) => new(path, "Finding", null, "Observed", null, null, null);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "boy-scout-firebase-store-" + Guid.NewGuid().ToString("N"));
        public string ProjectPath { get; }
        public BoyScoutDatabase Database { get; }
        public BoyScoutConfigService Config { get; }
        public ProjectSummary Project { get; }
        public Server Server { get; } = new();
        public ReportStore Store { get; }

        public Fixture(bool firebaseEnabled = true)
        {
            Directory.CreateDirectory(Root);
            ProjectPath = Path.Combine(Root, "project");
            Directory.CreateDirectory(ProjectPath);
            Database = new BoyScoutDatabase(Path.Combine(Root, "reports.db"));
            Database.Migrate();
            Config = new BoyScoutConfigService(Path.Combine(Root, "config.json"));
            Config.SaveConnection(new FirebaseConnectionInput("https://example.firebaseio.com/", "project", "source@example.com", "test-key"));
            Project = new ReportStore(Database).CreateProject("Local", ProjectPath);
            Config.Bind(1, ProjectPath);
            Config.SetAiReadConsent(1, true);
            if (firebaseEnabled) Config.SetFirebaseWriteEnabled(1, true);
            Server.Set("report-projects/1", "{\"Project\":{\"Id\":1,\"Name\":\"Remote\"},\"Notes\":{}}");
            Store = new ReportStore(Database, Config, connection => new FirebaseRealtimeDatabaseTransport(
                connection, Server.Client, _ => Task.FromResult("token")));
        }

        public void AddEnabledBinding(long id, string path)
        {
            Config.Bind(id, path);
            Config.SetAiReadConsent(id, true);
            Config.SetFirebaseWriteEnabled(id, true);
            Server.Set($"report-projects/{id}", $"{{\"Project\":{{\"Id\":{id},\"Name\":\"Remote {id}\"}},\"Notes\":{{}}}}");
        }

        public int LocalReportCount()
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM reports;";
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void Dispose()
        {
            Server.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Server : IDisposable
    {
        private readonly Dictionary<string, (string Json, int Version)> nodes = new(StringComparer.Ordinal);
        public HttpClient Client { get; } = null!;
        public bool FailNextProjectWrite { get; set; }
        public int ProjectWrites { get; private set; }

        public Server() => Client = new HttpClient(new Handler(Respond));
        public void Set(string path, string json) => nodes[path] = (json, nodes.TryGetValue(path, out var current) ? current.Version + 1 : 1);

        public void AddNote(long projectId, string id, string path, string status, string title)
        {
            var projectPath = $"report-projects/{projectId}";
            using var document = JsonDocument.Parse(nodes[projectPath].Json);
            var notes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(document.RootElement.GetProperty("Notes").GetRawText())!;
            var note = new { Id = id, ProjectId = projectId, OriginalPath = path, Title = title, Location = (string?)null,
                Observation = "Observed", Impact = (string?)null, SuggestedAction = (string?)null, DiscoveredDuring = (string?)null,
                Status = status, CreatedAt = "2026-10-01T00:00:00.000Z", UpdatedAt = "2026-10-01T00:00:00.000Z",
                SourceInstallationId = Guid.Parse("c0b25db3-6f62-4636-ae8a-e34a0c90871f"), SourceName = "remote-source" };
            notes[id] = JsonSerializer.SerializeToElement(note);
            Set(projectPath, JsonSerializer.Serialize(new { Project = new { Id = projectId, Name = "Remote" }, Notes = notes }));
        }

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            path = path[..^5];
            if (request.Method == HttpMethod.Get)
            {
                if (request.RequestUri.Query.Length > 0)
                {
                    var projectId = path.Split('/')[1];
                    using var project = JsonDocument.Parse(nodes[path[..path.LastIndexOf('/')]].Json);
                    var entries = project.RootElement.GetProperty("Notes").EnumerateObject()
                        .OrderBy(entry => entry.Name, StringComparer.Ordinal).TakeLast(101)
                        .ToDictionary(entry => entry.Name, entry => entry.Value.Clone());
                    return Json(JsonSerializer.Serialize(entries));
                }
                if (path.Contains("/Notes/", StringComparison.Ordinal))
                {
                    var segments = path.Split('/');
                    using var project = JsonDocument.Parse(nodes[$"report-projects/{segments[1]}"].Json);
                    var noteJson = project.RootElement.GetProperty("Notes").TryGetProperty(segments[3], out var note)
                        ? note.GetRawText() : "null";
                    var noteResponse = Json(noteJson);
                    noteResponse.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
                    return noteResponse;
                }
                var current = nodes.TryGetValue(path, out var stored) ? stored : ("null", 0);
                var response = Json(current.Item1);
                response.Headers.TryAddWithoutValidation("ETag", $"\"v{current.Item2}\"");
                return response;
            }

            var isProject = path.StartsWith("report-projects/", StringComparison.Ordinal);
            if (isProject) ProjectWrites++;
            if (isProject && FailNextProjectWrite)
            {
                FailNextProjectWrite = false;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using (JsonDocument.Parse(body)) { }
            Set(path, body);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
        public void Dispose() => Client.Dispose();
        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
        }
    }
}
