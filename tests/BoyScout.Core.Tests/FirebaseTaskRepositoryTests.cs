using System.Net;
using System.Text.Json.Nodes;
using BoyScout.Core.Storage.Firebase;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class FirebaseTaskRepositoryTests
{
    private const string Id = "BSR-1-0000000000000000001";
    private static readonly Guid Installation = Guid.Parse("c0b25db3-6f62-4636-ae8a-e34a0c90871f");
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Save_does_not_create_task_and_confirm_preserves_all_other_data()
    {
        using var server = new Server();
        var repo = Repository(server);
        var handle = await Acquire(server);
        var proposal = await repo.SaveReadyProposalAsync(handle, Id, "Title", "Description", "Criteria", "Verified",
            TestContext.Current.CancellationToken, currentConsequences: "Current behavior loses updates.");
        Assert.Equal("ready", proposal.State);
        Assert.Equal("Current behavior loses updates.", proposal.CurrentConsequences);
        Assert.NotNull(proposal.AgentCheckedAt);
        Assert.Null(proposal.HumanApprovedAt);
        Assert.Null(await repo.GetTaskAsync(Id, TestContext.Current.CancellationToken));
        Assert.Equal("open", server.Root["Notes"]![Id]!["Status"]!.GetValue<string>());

        var task = await repo.ConfirmProposalAsync(Id, Installation, " Laptop ", TestContext.Current.CancellationToken);
        Assert.Equal("todo", task.Status);
        Assert.Equal(new[] { Id }, task.SourceReportIds);
        Assert.Equal("Laptop", task.ConfirmerName);
        Assert.Equal(proposal.CurrentConsequences, task.CurrentConsequences);
        Assert.Equal(Installation, task.ConfirmerInstallationId);
        var confirmed = (await repo.GetProposalAsync(Id, TestContext.Current.CancellationToken))!;
        Assert.Equal("confirmed", confirmed.State);
        Assert.NotNull(confirmed.HumanApprovedAt);
        Assert.Null(confirmed.HumanRejectedAt);
        Assert.Equal(task.Id, (await repo.GetTaskAsync(Id, TestContext.Current.CancellationToken))!.Id);
        Assert.Equal("resolved", server.Root["Notes"]![Id]!["Status"]!.GetValue<string>());
        Assert.Equal("local", server.Root["Notes"]![Id]!["OriginalPath"]!.GetValue<string>());
        Assert.Equal("source", server.Root["Notes"]![Id]!["SourceName"]!.GetValue<string>());
        Assert.Equal("note-extra", server.Root["Notes"]![Id]!["Extra"]!.GetValue<string>());
        Assert.Equal("meta", server.Root["Metadata"]!["Custom"]!.GetValue<string>());
        Assert.Equal("keep", server.Root["Tasks"]!["BST-other"]!["Extra"]!.GetValue<string>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmProposalAsync(Id, Installation, "laptop", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_confirm_from_two_instances_creates_exactly_one_task()
    {
        using var server = new Server();
        var first = Repository(server);
        var second = Repository(server);
        await first.SaveReadyProposalAsync(await Acquire(server), Id, "Title", "Description", "Criteria", null, TestContext.Current.CancellationToken);
        var outcomes = await Task.WhenAll(new[] { first, second }.Select(async repo =>
        {
            try { await repo.ConfirmProposalAsync(Id, Installation, "name", TestContext.Current.CancellationToken); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.Equal(1, outcomes.Count(success => success));
        Assert.Equal(2, ((JsonObject)server.Root["Tasks"]!).Count);
        Assert.Equal("confirmed", server.Root["TaskProposals"]![Id]!["State"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stale_or_invalid_state_never_writes()
    {
        using var server = new Server();
        var repo = Repository(server);
        var handle = await Acquire(server);
        var state = server.Root;
        state["Notes"]![Id]!["Status"] = "dismissed";
        server.Root = state;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveReadyProposalAsync(handle, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        state = server.Root;
        state["Notes"]![Id]!["Status"] = "open";
        server.Root = state;
        await repo.SaveReadyProposalAsync(handle, Id, "t", "d", "a", null, TestContext.Current.CancellationToken);
        state = server.Root;
        state["TaskProposals"]![Id]!["RecommendedAction"] = "reject";
        server.Root = state;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmProposalAsync(Id, Installation, "name", TestContext.Current.CancellationToken));
        state = server.Root;
        state["TaskProposals"]![Id]!["RecommendedAction"] = "create_task";
        state["Notes"]![Id]!["Status"] = "resolved";
        server.Root = state;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmProposalAsync(Id, Installation, "name", TestContext.Current.CancellationToken));
        Assert.Null(server.Root["Tasks"]!["BST-" + Id]);
    }

    [Fact]
    public async Task Retries_412_only_and_does_not_retry_ambiguous_put()
    {
        using var server = new Server();
        var repo = Repository(server);
        var handle = await Acquire(server);
        server.ConflictNext = true;
        await repo.SaveReadyProposalAsync(handle, Id, "t", "d", "a", null, TestContext.Current.CancellationToken);
        Assert.Equal(3, server.Writes);
        server.ConflictNext = true;
        await repo.ConfirmProposalAsync(Id, Installation, "name", TestContext.Current.CancellationToken);
        Assert.Equal(5, server.Writes);

        using var other = new Server();
        var second = Repository(other);
        var otherHandle = await Acquire(other);
        other.ThrowNext = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => second.SaveReadyProposalAsync(otherHandle, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        Assert.Equal(2, other.Writes);
        Assert.Null(other.Root["TaskProposals"]);
    }

    [Fact]
    public async Task Worker_outcomes_are_saved_with_expected_fields_and_preserve_unrelated_root_data()
    {
        using var server = new Server();
        var repo = Repository(server);
        var handle = await Acquire(server);
        SetProcessingProposal(server);
        var current = await repo.SaveCurrentProposalAsync(handle, Id, " Title ", " Description ", " Criteria ",
            "Verified", TestContext.Current.CancellationToken, "Consequences", "Localized title", "Localized observation");
        Assert.Equal("ready", current.State);
        Assert.Equal("current", current.Relevance);
        Assert.Equal("create_task", current.RecommendedAction);
        Assert.Equal("Title", current.Title);
        Assert.Equal("Localized title", current.LocalizedReportTitle);

        SetProcessingProposal(server);
        var rejection = await repo.SaveRejectionRecommendationAsync(handle, Id, "Independently verified obsolete",
            "Checked upstream and downstream references.", TestContext.Current.CancellationToken);
        Assert.Equal("ready", rejection.State);
        Assert.Equal("obsolete", rejection.Relevance);
        Assert.Equal("reject", rejection.RecommendedAction);
        Assert.Equal("Independently verified obsolete", rejection.FinalRejectionReason);
        Assert.Null(rejection.Title);
        Assert.Null(rejection.CurrentConsequences);

        SetProcessingProposal(server);
        var unverified = await repo.MarkUnverifiedAsync(handle, Id, "Insufficient evidence.",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("unverified", unverified.State);
        Assert.Null(unverified.RecommendedAction);
        Assert.Equal("Insufficient evidence.", unverified.VerificationSummary);

        SetProcessingProposal(server);
        var failed = await repo.FailProposalAsync(handle, Id, "Generator failed", TestContext.Current.CancellationToken);
        Assert.Equal("failed", failed.State);
        Assert.Equal("Generator failed", failed.Error);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal("meta", server.Root["Metadata"]!["Custom"]!.GetValue<string>());
        Assert.Equal("keep", server.Root["Tasks"]!["BST-other"]!["Extra"]!.GetValue<string>());
        Assert.Equal("note-extra", server.Root["Notes"]![Id]!["Extra"]!.GetValue<string>());
    }

    [Fact]
    public async Task Every_worker_result_rejects_stale_mismatched_and_expired_leases()
    {
        using var server = new Server();
        var repo = Repository(server);
        var lease = Lease(server);
        var old = (await lease.AcquireAsync(Id, Installation.ToString(), "worker", TestContext.Current.CancellationToken))!;
        await lease.ForceReleaseAsync(Id, TestContext.Current.CancellationToken);
        var writes = server.Writes;
        var results = new Func<Task<FirebaseTaskProposal>>[]
        {
            () => repo.SaveCurrentProposalAsync(old, Id, "t", "d", "a", null, TestContext.Current.CancellationToken),
            () => repo.SaveRejectionRecommendationAsync(old, Id, "reason", "verified", TestContext.Current.CancellationToken),
            () => repo.MarkUnverifiedAsync(old, Id, "uncertain", cancellationToken: TestContext.Current.CancellationToken),
            () => repo.FailProposalAsync(old, Id, "error", TestContext.Current.CancellationToken)
        };
        foreach (var result in results)
            await Assert.ThrowsAsync<InvalidOperationException>(result);
        Assert.Equal(writes, server.Writes);

        var currentLease = (await lease.AcquireAsync(Id, Installation.ToString(), "worker", TestContext.Current.CancellationToken))!;
        var mismatched = currentLease with { OwnerName = "other" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.FailProposalAsync(mismatched, Id, "error", TestContext.Current.CancellationToken));
        server.Now = currentLease.ExpiresAt;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.FailProposalAsync(currentLease, Id, "error", TestContext.Current.CancellationToken));
        Assert.Null(server.Root["TaskProposals"]);
    }

    [Fact]
    public async Task Worker_result_does_not_create_a_missing_proposal()
    {
        using var server = new Server();
        var repo = Repository(server);
        var handle = await Acquire(server);
        var writes = server.Writes;

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveCurrentProposalAsync(handle, Id,
            "Title", "Description", "Criteria", null, TestContext.Current.CancellationToken));

        Assert.Equal(writes, server.Writes);
        Assert.Null(server.Root["TaskProposals"]);
    }

    [Fact]
    public async Task Worker_result_does_not_overwrite_a_ready_proposal()
    {
        using var server = new Server();
        var repo = Repository(server);
        var handle = await Acquire(server);
        await repo.SaveReadyProposalAsync(handle, Id, "Existing", "Description", "Criteria", null,
            TestContext.Current.CancellationToken);
        var writes = server.Writes;
        var original = server.Root["TaskProposals"]![Id]!.ToJsonString();

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.FailProposalAsync(handle, Id,
            "New result", TestContext.Current.CancellationToken));

        Assert.Equal(writes, server.Writes);
        Assert.Equal(original, server.Root["TaskProposals"]![Id]!.ToJsonString());
    }

    [Fact]
    public async Task Worker_result_retries_project_cas_conflict_and_reads_legacy_current_ready_proposals()
    {
        using var server = new Server();
        var repo = Repository(server);
        var handle = await Acquire(server);
        SetProcessingProposal(server);
        server.ConflictNext = true;
        await repo.SaveCurrentProposalAsync(handle, Id, "Title", "Description", "Criteria", null,
            TestContext.Current.CancellationToken);
        Assert.Equal(3, server.Writes);
        var state = server.Root;
        state["TaskProposals"]![Id] = JsonNode.Parse("""
            {"ReportId":"BSR-1-0000000000000000001","ProjectId":1,"State":"ready",
             "Relevance":"current","RecommendedAction":"create_task","Title":"Old title",
             "Description":"Old description","AcceptanceCriteria":"Old criteria",
             "CreatedAt":"2025-01-01T00:00:00.000Z","UpdatedAt":"2025-01-01T00:00:00.000Z"}
            """);
        server.Root = state;
        var legacy = await repo.GetProposalAsync(Id, TestContext.Current.CancellationToken);
        Assert.Equal("Old title", legacy!.Title);
        Assert.Equal(0, legacy.Attempts);
    }

    [Fact]
    public async Task Malformed_collections_or_existing_link_fail_closed()
    {
        using var server = new Server();
        var repo = Repository(server);
        var handle = await Acquire(server);
        var state = server.Root;
        state["Tasks"]!["BST-other"]!["SourceReportIds"] = new JsonArray(Id);
        server.Root = state;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveReadyProposalAsync(handle, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        state = server.Root;
        state["Tasks"]!["BST-other"]!["SourceReportIds"] = new JsonArray("other");
        state["Tasks"] = new JsonArray();
        server.Root = state;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveReadyProposalAsync(handle, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        Assert.Equal(1, server.Writes);
    }

    [Fact]
    public async Task Release_between_proposal_read_and_put_rejects_stale_completion()
    {
        using var server = new Server();
        var lease = Lease(server);
        var old = (await lease.AcquireAsync(Id, Installation.ToString(), "worker", TestContext.Current.CancellationToken))!;
        server.BeforeNextPut = () => lease.ForceReleaseAsync(Id, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(server).SaveReadyProposalAsync(old, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        Assert.Null(server.Root["TaskProposals"]);
        var next = (await lease.AcquireAsync(Id, "other-installation", "other", TestContext.Current.CancellationToken))!;
        Assert.Equal(3, next.Generation);
        Assert.NotEqual(old.Token, next.Token);
        Assert.Equal("meta", server.Root["Metadata"]!["Custom"]!.GetValue<string>());
        Assert.Equal("keep", server.Root["Tasks"]!["BST-other"]!["Extra"]!.GetValue<string>());
    }

    [Fact]
    public async Task Contention_renew_expiry_and_missing_date_are_fenced()
    {
        using var server = new Server();
        var first = Lease(server);
        var second = Lease(server);
        var old = (await first.AcquireAsync(Id, "machine-a", "a", TestContext.Current.CancellationToken))!;
        Assert.Null(await second.AcquireAsync(Id, "machine-b", "b", TestContext.Current.CancellationToken));
        server.Now = Start.AddMinutes(1);
        var renewed = (await first.RenewAsync(old, TestContext.Current.CancellationToken))!;
        Assert.Equal(old.Token, renewed.Token);
        Assert.Equal(old.Generation, renewed.Generation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(server).SaveReadyProposalAsync(old, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        server.Now = renewed.ExpiresAt;
        Assert.Null(await first.RenewAsync(renewed, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(server).SaveReadyProposalAsync(renewed, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        var next = (await second.AcquireAsync(Id, "machine-b", "b", TestContext.Current.CancellationToken))!;
        Assert.Equal(2, next.Generation);
        server.HasDate = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(server).SaveReadyProposalAsync(next, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.RenewAsync(next, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.ForceReleaseAsync(Id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.AcquireAsync(Id, "machine-a", "a", TestContext.Current.CancellationToken));
        Assert.Null(server.Root["TaskProposals"]);
    }

    [Fact]
    public async Task Force_release_fences_completion_and_allows_immediate_reacquisition()
    {
        using var server = new Server();
        var lease = Lease(server);
        server.ConflictNext = true;
        var old = (await lease.AcquireAsync(Id, "machine-a", "a", TestContext.Current.CancellationToken))!;
        Assert.Equal(1, old.Generation);
        Assert.Equal(2, server.Writes);
        await lease.ForceReleaseAsync(Id, TestContext.Current.CancellationToken);
        Assert.Null(await lease.RenewAsync(old, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(server).SaveReadyProposalAsync(old, Id, "t", "d", "a", null, TestContext.Current.CancellationToken));
        var next = (await lease.AcquireAsync(Id, "machine-b", "b", TestContext.Current.CancellationToken))!;
        Assert.Equal(3, next.Generation);
        Assert.Null(server.Root["TaskProposals"]);
        Assert.Equal("note-extra", server.Root["Notes"]![Id]!["Extra"]!.GetValue<string>());
    }

    private static FirebaseProjectLease Lease(Server server) => new(new FirebaseRealtimeDatabaseTransport(
        new FirebaseConnection("https://example.firebaseio.com/", "project", "service@example.com", "test-key"),
        server.Client, _ => Task.FromResult("token")), TimeSpan.FromMinutes(2));

    private static async Task<FirebaseProjectLeaseHandle> Acquire(Server server) =>
        (await Lease(server).AcquireAsync(Id, Installation.ToString(), "worker", TestContext.Current.CancellationToken))!;

    private static FirebaseTaskRepository Repository(Server server) => new(new FirebaseRealtimeDatabaseTransport(
        new FirebaseConnection("https://example.firebaseio.com/", "project", "service@example.com", "test-key"),
        server.Client, _ => Task.FromResult("token")));

    private static void SetProcessingProposal(Server server)
    {
        var root = server.Root;
        root["TaskProposals"] ??= new JsonObject();
        root["TaskProposals"]![Id] = JsonNode.Parse("""
            {"ReportId":"BSR-1-0000000000000000001","ProjectId":1,"State":"processing",
             "CreatedAt":"2025-01-01T00:00:00.000Z","UpdatedAt":"2025-01-01T00:00:00.000Z","Attempts":1}
            """);
        server.Root = root;
    }

    private sealed class Server : IDisposable
    {
        private readonly object gate = new();
        private JsonObject root = JsonNode.Parse("""
            {"Project":{"Id":1,"Name":"P"},"Notes":{"BSR-1-0000000000000000001":{
              "Id":"BSR-1-0000000000000000001","ProjectId":1,"Status":"open",
              "OriginalPath":"local","SourceName":"source","Extra":"note-extra"}},
             "Metadata":{"Custom":"meta"},"Tasks":{"BST-other":{"SourceReportIds":["other"],"Extra":"keep"}}}
            """)!.AsObject();
        private int version;
        public HttpClient Client { get; }
        public bool ConflictNext { get; set; }
        public bool ThrowNext { get; set; }
        public int Writes { get; private set; }
        public DateTimeOffset Now { get; set; } = Start;
        public bool HasDate { get; set; } = true;
        public Action? BeforeNextPut { get; set; }
        public JsonObject Root { get { lock (gate) return root.DeepClone().AsObject(); } set { lock (gate) { root = value; version++; } } }

        public Server() => Client = new HttpClient(new Handler(this));

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            lock (gate)
            {
                Assert.Equal("/report-projects/1.json", request.RequestUri!.AbsolutePath);
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Equal("true", Assert.Single(request.Headers.GetValues("X-Firebase-ETag")));
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(root.ToJsonString()) };
                    response.Headers.TryAddWithoutValidation("ETag", $"\"v{version}\"");
                    if (HasDate) response.Headers.Date = Now;
                    return response;
                }
                Assert.Equal(HttpMethod.Put, request.Method);
                Writes++;
                if (BeforeNextPut is { } action)
                {
                    BeforeNextPut = null;
                    // The fake simulates a separate machine changing the root before this PUT.
                    System.Threading.Monitor.Exit(gate);
                    try { action(); }
                    finally { System.Threading.Monitor.Enter(gate); }
                }
                if (ThrowNext) { ThrowNext = false; throw new HttpRequestException("Ambiguous write"); }
                if (ConflictNext) { ConflictNext = false; version++; return new HttpResponseMessage(HttpStatusCode.PreconditionFailed); }
                if (Assert.Single(request.Headers.GetValues("if-match")) != $"\"v{version}\"")
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                root = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
                version++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        public void Dispose() => Client.Dispose();

        private sealed class Handler(Server server) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(server.Respond(request));
        }
    }
}
