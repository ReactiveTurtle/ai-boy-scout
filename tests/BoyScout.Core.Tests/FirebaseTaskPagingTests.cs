using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoyScout.Infrastructure.Firebase;
using BoyScout.Domain.Tasks;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class FirebaseTaskPagingTests
{
    private static string ReportId(long project, long sequence) => $"BSR-{project}-{sequence:D19}";
    private static string TaskId(long project, long sequence) => "BST-" + ReportId(project, sequence);

    [Fact]
    public async Task Pages_only_project_tasks_in_key_order_without_an_empty_terminal_page()
    {
        using var server = new Server();
        for (var i = 1; i <= 5; i++) server.Add(1, i);
        server.Add(2, 99);
        var repo = Repository(server);

        var first = await repo.BrowseTasksAsync(1, null, 2, TestContext.Current.CancellationToken);
        Assert.Equal([TaskId(1, 5), TaskId(1, 4)], first.Items.Select(task => task.Id));
        Assert.Equal(TaskId(1, 4), first.NextCursor);
        server.Add(1, 6); // A newer concurrent insertion cannot enter the remaining keyset pages.
        var second = await repo.BrowseTasksAsync(1, first.NextCursor, 2, TestContext.Current.CancellationToken);
        var third = await repo.BrowseTasksAsync(1, second.NextCursor, 2, TestContext.Current.CancellationToken);
        Assert.Equal([TaskId(1, 3), TaskId(1, 2)], second.Items.Select(task => task.Id));
        Assert.Equal([TaskId(1, 1)], third.Items.Select(task => task.Id));
        Assert.Null(third.NextCursor);
        Assert.Equal([(1L, (string?)null, 3), (1L, TaskId(1, 4), 4), (1L, TaskId(1, 2), 4)], server.Requests);
    }

    [Fact]
    public async Task Exact_page_boundary_and_missing_cursor_do_not_claim_another_page()
    {
        using var server = new Server();
        for (var i = 1; i <= 4; i++) server.Add(1, i);
        var repo = Repository(server);
        var first = await repo.BrowseTasksAsync(1, null, 2, TestContext.Current.CancellationToken);
        var last = await repo.BrowseTasksAsync(1, first.NextCursor, 2, TestContext.Current.CancellationToken);
        Assert.Equal([TaskId(1, 2), TaskId(1, 1)], last.Items.Select(task => task.Id));
        Assert.Null(last.NextCursor);
        var missing = await repo.BrowseTasksAsync(1, TaskId(1, 10), 2, TestContext.Current.CancellationToken);
        Assert.Equal([TaskId(1, 4), TaskId(1, 3)], missing.Items.Select(task => task.Id));
        Assert.Equal(TaskId(1, 3), missing.NextCursor);
        Assert.Null((await repo.BrowseTasksAsync(2, null, 10, TestContext.Current.CancellationToken)).NextCursor);
    }

    [Theory]
    [InlineData(0, null, 10)]
    [InlineData(1, null, 0)]
    [InlineData(1, null, 101)]
    [InlineData(1, "BST-BSR-2-0000000000000000001", 10)]
    [InlineData(1, "BST-BSR-1-1", 10)]
    [InlineData(1, "BSR-1-0000000000000000001", 10)]
    public async Task Invalid_arguments_never_request_http(long projectId, string? before, int limit)
    {
        using var server = new Server();
        var repo = Repository(server);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.BrowseTasksAsync(projectId, before, limit, TestContext.Current.CancellationToken));
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("Id", "BST-BSR-2-0000000000000000001")]
    [InlineData("ProjectId", 2)]
    [InlineData("Status", "unknown")]
    [InlineData("Title", "")]
    [InlineData("SourceReportIds", "wrong")]
    public async Task Rejects_invalid_task_fields(string field, object value)
    {
        using var server = new Server();
        server.Add(1, 1);
        server.Change(1, 1, node => node[field] = field == "SourceReportIds" ? new JsonArray((string)value) : JsonSerializer.SerializeToNode(value));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(server).BrowseTasksAsync(1, null, 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_malformed_link_and_collection_shape()
    {
        using var server = new Server();
        server.Add(1, 1);
        server.Change(1, 1, node => node["SourceReportIds"] = new JsonArray(ReportId(2, 1)));
        var repo = Repository(server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.BrowseTasksAsync(1, null, 10, TestContext.Current.CancellationToken));
        server.RawResponse = "[]";
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.BrowseTasksAsync(1, null, 10, TestContext.Current.CancellationToken));
        server.RawResponse = "null";
        Assert.Empty((await repo.BrowseTasksAsync(1, null, 10, TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task Rejects_source_link_that_does_not_identify_the_task_note()
    {
        using var server = new Server();
        server.Add(1, 1);
        server.Change(1, 1, node => node["SourceReportIds"] = new JsonArray(ReportId(1, 2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(server).BrowseTasksAsync(1, null, 10, TestContext.Current.CancellationToken));
    }

    private static FirebaseTaskRepository Repository(Server server) => new(new FirebaseRealtimeDatabaseTransport(
        new FirebaseConnection("https://example.firebaseio.com/", "project", "service@example.com", "test-key"),
        server.Client, _ => Task.FromResult("token")));

    private sealed class Server : IDisposable
    {
        private readonly Dictionary<long, JsonObject> tasks = new();
        public HttpClient Client { get; }
        public string? RawResponse { get; set; }
        public List<(long ProjectId, string? Before, int Limit)> Requests { get; } = new();

        public Server() => Client = new HttpClient(new Handler(this));

        public void Add(long projectId, long sequence)
        {
            if (!tasks.TryGetValue(projectId, out var collection)) tasks[projectId] = collection = new JsonObject();
            var id = TaskId(projectId, sequence);
            collection[id] = JsonSerializer.SerializeToNode(new BoyScoutTask(id, projectId, "Title", "Description",
                "Criteria", TaskStatuses.Todo, "2026-01-01", "2026-01-01", [ReportId(projectId, sequence)]));
        }

        public void Change(long projectId, long sequence, Action<JsonObject> change) => change((JsonObject)tasks[projectId][TaskId(projectId, sequence)]!);

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.False(request.Headers.Contains("X-Firebase-ETag"));
            var path = request.RequestUri!.AbsolutePath.Split('/');
            Assert.Equal(["", "report-projects", path[2], "Tasks.json"], path);
            var projectId = long.Parse(path[2]);
            var parameters = request.RequestUri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
            Assert.Equal("\"$key\"", parameters["orderBy"]);
            Assert.Equal(parameters.ContainsKey("endAt") ? 3 : 2, parameters.Count);
            var limit = int.Parse(parameters["limitToLast"]);
            Assert.InRange(limit, 1, 102);
            var before = parameters.TryGetValue("endAt", out var endAt) ? JsonSerializer.Deserialize<string>(endAt) : null;
            Requests.Add((projectId, before, limit));
            var entries = tasks.GetValueOrDefault(projectId)?.Where(entry => before is null || string.CompareOrdinal(entry.Key, before) <= 0)
                .OrderBy(entry => entry.Key, StringComparer.Ordinal).TakeLast(limit)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RawResponse ?? (entries is null || entries.Count == 0 ? "null" : JsonSerializer.Serialize(entries)))
            };
        }

        public void Dispose() => Client.Dispose();

        private sealed class Handler(Server server) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(server.Respond(request));
        }
    }
}
