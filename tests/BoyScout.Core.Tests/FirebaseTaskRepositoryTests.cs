using System.Net;
using System.Text.Json.Nodes;
using BoyScout.Core.Storage.Config;
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
    public async Task Confirm_rejection_atomically_dismisses_note_and_preserves_final_and_unrelated_data()
    {
        using var server = new Server();
        var repo = Repository(server);
        await SaveRejection(repo, server);
        var state = server.Root;
        state["TaskProposals"]![Id]!["HumanApprovedAt"] = "previous-approval";
        state["TaskProposals"]![Id]!["CurrentConsequences"] = "clear this";
        state["TaskProposals"]![Id]!["Extra"] = "proposal-extra";
        server.Root = state;
        var writes = server.Writes;

        var rejected = await repo.ConfirmRejectionAsync(Id, TestContext.Current.CancellationToken);

        Assert.Equal("rejected", rejected.State);
        Assert.Equal("Independently verified obsolete", rejected.FinalRejectionReason);
        Assert.Equal("Checked upstream and downstream references.", rejected.VerificationSummary);
        Assert.NotNull(rejected.HumanRejectedAt);
        Assert.Null(rejected.HumanApprovedAt);
        Assert.Null(rejected.CurrentConsequences);
        Assert.Equal(rejected.HumanRejectedAt, rejected.UpdatedAt);
        Assert.Equal(rejected.UpdatedAt, server.Root["Notes"]![Id]!["UpdatedAt"]!.GetValue<string>());
        Assert.Equal("dismissed", server.Root["Notes"]![Id]!["Status"]!.GetValue<string>());
        Assert.Equal(writes + 1, server.Writes);
        Assert.Equal("proposal-extra", server.Root["TaskProposals"]![Id]!["Extra"]!.GetValue<string>());
        Assert.Equal("note-extra", server.Root["Notes"]![Id]!["Extra"]!.GetValue<string>());
        Assert.Equal("meta", server.Root["Metadata"]!["Custom"]!.GetValue<string>());
        Assert.Equal("keep", server.Root["Tasks"]!["BST-other"]!["Extra"]!.GetValue<string>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmRejectionAsync(Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Confirm_rejection_refuses_missing_incompatible_current_and_inactive_inputs_without_writing()
    {
        foreach (var scenario in new[] { "missing", "incompatible", "current", "inactive" })
        {
            using var server = new Server();
            var repo = Repository(server);
            await SaveRejection(repo, server);
            var state = server.Root;
            if (scenario == "missing") state["TaskProposals"]!.AsObject().Remove(Id);
            if (scenario == "incompatible")
            {
                state["TaskProposals"]![Id]!["State"] = "unverified";
                state["TaskProposals"]![Id]!["Relevance"] = "current";
                state["TaskProposals"]![Id]!["RecommendedAction"] = null;
                state["TaskProposals"]![Id]!["FinalRejectionReason"] = null;
            }
            if (scenario == "current")
            {
                state["TaskProposals"]![Id]!["State"] = "ready";
                state["TaskProposals"]![Id]!["Relevance"] = "current";
                state["TaskProposals"]![Id]!["RecommendedAction"] = "create_task";
                state["TaskProposals"]![Id]!["Title"] = "Current title";
                state["TaskProposals"]![Id]!["Description"] = "Current description";
                state["TaskProposals"]![Id]!["AcceptanceCriteria"] = "Current criteria";
                state["TaskProposals"]![Id]!["FinalRejectionReason"] = null;
            }
            if (scenario == "inactive") state["Notes"]![Id]!["Status"] = "resolved";
            server.Root = state;
            var writes = server.Writes;

            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmRejectionAsync(Id,
                TestContext.Current.CancellationToken));

            Assert.Equal(writes, server.Writes);
        }
    }

    [Fact]
    public async Task Confirm_rejection_retries_project_cas_conflict()
    {
        using var server = new Server();
        var repo = Repository(server);
        await SaveRejection(repo, server);
        server.ConflictNext = true;

        await repo.ConfirmRejectionAsync(Id, TestContext.Current.CancellationToken);

        Assert.Equal("rejected", server.Root["TaskProposals"]![Id]!["State"]!.GetValue<string>());
        Assert.Equal("dismissed", server.Root["Notes"]![Id]!["Status"]!.GetValue<string>());
        Assert.Equal(4, server.Writes);
    }

    [Fact]
    public async Task Retry_proposal_clears_results_preserves_review_intent_and_fences_old_worker()
    {
        using var server = new Server();
        var repo = Repository(server);
        var worker = await Acquire(server);
        SetReadyProposal(server, "ready");
        var state = server.Root;
        state["TaskProposals"]![Id]!["ReviewIntent"] = "rejection";
        state["TaskProposals"]![Id]!["RequestedRejectionReason"] = "retain requested reason";
        state["TaskProposals"]![Id]!["RequestedClarification"] = "old clarification";
        state["TaskProposals"]![Id]!["LocalizedReportTitle"] = "Localized title";
        state["TaskProposals"]![Id]!["Extra"] = "proposal-extra";
        state["TaskProposals"]![Id]!["HumanApprovedAt"] = "approved";
        state["TaskProposals"]![Id]!["HumanRejectedAt"] = "rejected";
        server.Root = state;

        var queued = await repo.RetryProposalAsync(Id, "  please retry  ", TestContext.Current.CancellationToken);

        Assert.Equal("queued", queued.State);
        Assert.Equal("please retry", queued.RequestedClarification);
        Assert.Equal("rejection", queued.ReviewIntent);
        Assert.Equal("retain requested reason", queued.RequestedRejectionReason);
        Assert.Equal(2, queued.Attempts);
        Assert.Equal("2025-01-01T00:00:00.000Z", queued.CreatedAt);
        Assert.Equal("Localized title", queued.LocalizedReportTitle);
        Assert.Null(queued.Title);
        Assert.Null(queued.Description);
        Assert.Null(queued.AcceptanceCriteria);
        Assert.Null(queued.VerificationSummary);
        Assert.Null(queued.Relevance);
        Assert.Null(queued.RecommendedAction);
        Assert.Null(queued.CurrentConsequences);
        Assert.Null(queued.FinalRejectionReason);
        Assert.Null(queued.Error);
        Assert.Null(queued.AgentCheckedAt);
        Assert.Null(queued.HumanApprovedAt);
        Assert.Null(queued.HumanRejectedAt);
        Assert.Equal("proposal-extra", server.Root["TaskProposals"]![Id]!["Extra"]!.GetValue<string>());
        Assert.Equal("note-extra", server.Root["Notes"]![Id]!["Extra"]!.GetValue<string>());
        Assert.Equal("meta", server.Root["Metadata"]!["Custom"]!.GetValue<string>());
        Assert.Equal("keep", server.Root["Tasks"]!["BST-other"]!["Extra"]!.GetValue<string>());
        Assert.Null(await Lease(server).RenewAsync(worker, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SaveReadyProposalAsync(worker, Id,
            "stale", "description", "criteria", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Request_clarification_sets_proposal_intent_and_retries_cas_conflict()
    {
        using var server = new Server();
        var repo = Repository(server);
        SetReadyProposal(server, "failed");
        var state = server.Root;
        state["TaskProposals"]![Id]!["ReviewIntent"] = "rejection";
        state["TaskProposals"]![Id]!["RequestedRejectionReason"] = "clear this";
        state["TaskProposals"]![Id]!["Error"] = "previous error";
        server.Root = state;
        server.ConflictNext = true;

        var queued = await repo.RequestClarificationAsync(Id, "  clarify impact  ", TestContext.Current.CancellationToken);

        Assert.Equal("queued", queued.State);
        Assert.Equal("proposal", queued.ReviewIntent);
        Assert.Null(queued.RequestedRejectionReason);
        Assert.Equal("clarify impact", queued.RequestedClarification);
        Assert.Equal(2, queued.Attempts);
        Assert.Null(queued.Error);
        Assert.Equal(2, server.Writes);
        Assert.Equal("note-extra", server.Root["Notes"]![Id]!["Extra"]!.GetValue<string>());
    }

    [Fact]
    public async Task Retry_and_clarification_reject_invalid_comments_and_proposal_states_without_writing()
    {
        foreach (var invalidComment in new[] { " ", new string('x', 2_001) })
        {
            using var commentServer = new Server();
            var commentRepo = Repository(commentServer);
            SetReadyProposal(commentServer, "ready");
            var writes = commentServer.Writes;
            await Assert.ThrowsAsync<ArgumentException>(() => commentRepo.RetryProposalAsync(Id, invalidComment,
                TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => commentRepo.RequestClarificationAsync(Id, invalidComment,
                TestContext.Current.CancellationToken));
            Assert.Equal(writes, commentServer.Writes);
        }

        foreach (var scenario in new[] { "queued", "processing", "confirmed", "rejected", "missing", "inactive" })
        {
            using var server = new Server();
            var repo = Repository(server);
            SetReadyProposal(server, "ready");
            var state = server.Root;
            if (scenario == "queued" || scenario == "processing" || scenario == "confirmed" || scenario == "rejected")
                state["TaskProposals"]![Id]!["State"] = scenario;
            if (scenario == "missing") state["TaskProposals"]!.AsObject().Remove(Id);
            if (scenario == "inactive") state["Notes"]![Id]!["Status"] = "resolved";
            server.Root = state;
            var writes = server.Writes;
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RetryProposalAsync(Id, "retry",
                TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RequestClarificationAsync(Id, "clarify",
                TestContext.Current.CancellationToken));
            Assert.Equal(writes, server.Writes);
        }
    }

    [Fact]
    public async Task Concurrent_rejection_confirmation_from_two_instances_has_exactly_one_success()
    {
        using var server = new Server();
        await SaveRejection(Repository(server), server);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await Repository(server).ConfirmRejectionAsync(Id, TestContext.Current.CancellationToken);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }));

        Assert.Equal(1, outcomes.Count(success => success));
        Assert.Equal("rejected", server.Root["TaskProposals"]![Id]!["State"]!.GetValue<string>());
        Assert.Equal("dismissed", server.Root["Notes"]![Id]!["Status"]!.GetValue<string>());
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

    [Fact]
    public async Task Claim_requires_consent_existing_binding_path_matching_note_path_and_active_note()
    {
        using var server = new Server();
        using var directory = BoundDirectory(server);
        var repo = Repository(server);
        var binding = Binding(directory.Path);

        Assert.Null(await repo.ClaimNextEligibleNoteAsync(binding with { AiReadConsent = false }, "worker", "name",
            TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        Assert.Null(await repo.ClaimNextEligibleNoteAsync(binding with { LocalPath = Path.Combine(directory.Path, "missing") },
            "worker", "name", TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        var state = server.Root;
        state["Notes"]![Id]!["OriginalPath"] = Path.Combine(directory.Path, "elsewhere");
        server.Root = state;
        Assert.Null(await repo.ClaimNextEligibleNoteAsync(binding, "worker", "name", TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        state = server.Root;
        state["Notes"]![Id]!["OriginalPath"] = directory.Path;
        state["Notes"]![Id]!["Status"] = "dismissed";
        server.Root = state;
        Assert.Null(await repo.ClaimNextEligibleNoteAsync(binding, "worker", "name", TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        Assert.Null(server.Root["TaskProposals"]);
    }

    [Fact]
    public async Task Claim_excludes_notes_with_direct_or_linked_tasks_and_ready_proposals()
    {
        using var server = new Server();
        using var directory = BoundDirectory(server);
        var repo = Repository(server);
        var binding = Binding(directory.Path);
        var state = server.Root;
        state["Tasks"]!["BST-" + Id] = new JsonObject { ["SourceReportIds"] = new JsonArray() };
        server.Root = state;
        Assert.Null(await repo.ClaimNextEligibleNoteAsync(binding, "worker", "name", TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        state = server.Root;
        state["Tasks"]!.AsObject().Remove("BST-" + Id);
        state["Tasks"]!["BST-other"]!["SourceReportIds"] = new JsonArray(Id);
        server.Root = state;
        Assert.Null(await repo.ClaimNextEligibleNoteAsync(binding, "worker", "name", TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        state = server.Root;
        state["Tasks"]!["BST-other"]!["SourceReportIds"] = new JsonArray("other");
        server.Root = state;
        await repo.SaveReadyProposalAsync(await Acquire(server), Id, "ready", "description", "criteria", null,
            TestContext.Current.CancellationToken);
        Assert.Null(await repo.ClaimNextEligibleNoteAsync(binding, "worker", "name", TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        Assert.Equal("ready", server.Root["TaskProposals"]![Id]!["State"]!.GetValue<string>());
    }

    [Fact]
    public async Task Claim_creates_initial_processing_proposal_with_attempt_one()
    {
        using var server = new Server();
        using var directory = BoundDirectory(server);
        var claim = await Repository(server).ClaimNextEligibleNoteAsync(Binding(directory.Path), "worker", "name",
            TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.NotNull(claim);
        Assert.Equal(Id, claim.Note["Id"]!.GetValue<string>());
        Assert.Equal("processing", claim.Proposal.State);
        Assert.Equal(1, claim.Proposal.Attempts);
        Assert.Equal(Id, claim.Lease.ReportId);
        Assert.Equal("processing", server.Root["TaskProposals"]![Id]!["State"]!.GetValue<string>());
        Assert.Equal(claim.Proposal.CreatedAt, claim.Proposal.UpdatedAt);
    }

    [Fact]
    public async Task Claim_selects_oldest_note_before_id_order()
    {
        using var server = new Server();
        using var directory = BoundDirectory(server);
        const string olderId = "BSR-1-0000000000000000002";
        var root = server.Root;
        var olderNote = root["Notes"]![Id]!.DeepClone().AsObject();
        olderNote["Id"] = olderId;
        olderNote["CreatedAt"] = "2024-01-01T00:00:00.000Z";
        root["Notes"]![olderId] = olderNote;
        server.Root = root;

        var claim = await Repository(server).ClaimNextEligibleNoteAsync(Binding(directory.Path), "worker", "name",
            TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.NotNull(claim);
        Assert.Equal(olderId, claim.Note["Id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Claim_queued_proposal_preserves_attempts_and_review_intent()
    {
        using var server = new Server();
        using var directory = BoundDirectory(server);
        var state = server.Root;
        state["TaskProposals"] = new JsonObject
        {
            [Id] = JsonNode.Parse("""
                {"ReportId":"BSR-1-0000000000000000001","ProjectId":1,"State":"queued",
                 "CreatedAt":"2025-01-01T00:00:00.000Z","UpdatedAt":"2025-01-02T00:00:00.000Z",
                 "Attempts":3,"ReviewIntent":"verify independently","RequestedClarification":"check impact",
                 "RequestedRejectionReason":"not obsolete","CurrentConsequences":"preserve this context"}
                """)
        };
        server.Root = state;

        var claim = await Repository(server).ClaimNextEligibleNoteAsync(Binding(directory.Path), "worker", "name",
            TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.NotNull(claim);
        Assert.Equal(3, claim.Proposal.Attempts);
        Assert.Equal("verify independently", claim.Proposal.ReviewIntent);
        Assert.Equal("check impact", claim.Proposal.RequestedClarification);
        Assert.Equal("not obsolete", claim.Proposal.RequestedRejectionReason);
        Assert.Equal("preserve this context", claim.Proposal.CurrentConsequences);
        Assert.Equal("processing", claim.Proposal.State);
    }

    [Fact]
    public async Task Claim_recovers_processing_proposal_only_after_old_lease_expires()
    {
        using var server = new Server();
        using var directory = BoundDirectory(server);
        SetProcessingProposal(server);
        var state = server.Root;
        state["TaskProposals"]![Id]!["Attempts"] = 4;
        server.Root = state;
        var oldLease = (await Lease(server).AcquireAsync(Id, "old-worker", "old", TestContext.Current.CancellationToken))!;
        Assert.Null(await Repository(server).ClaimNextEligibleNoteAsync(Binding(directory.Path), "worker", "name",
            TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        server.Now = oldLease.ExpiresAt;

        var claim = await Repository(server).ClaimNextEligibleNoteAsync(Binding(directory.Path), "worker", "name",
            TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.NotNull(claim);
        Assert.Equal(4, claim.Proposal.Attempts);
        Assert.NotEqual(oldLease.Token, claim.Lease.Token);
    }

    [Fact]
    public async Task Concurrent_claimers_create_only_one_processing_claim()
    {
        using var server = new Server();
        using var directory = BoundDirectory(server);
        var binding = Binding(directory.Path);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Repository(server)
            .ClaimNextEligibleNoteAsync(binding, "worker", "name", TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken)));

        Assert.Single(results, result => result is not null);
        Assert.Equal("processing", server.Root["TaskProposals"]![Id]!["State"]!.GetValue<string>());
    }

    [Fact]
    public async Task Claim_retries_cas_precondition_conflict_without_overwriting_new_proposal_state()
    {
        using var server = new Server();
        using var directory = BoundDirectory(server);
        server.ConflictWriteNumber = 2;
        var claim = await Repository(server).ClaimNextEligibleNoteAsync(Binding(directory.Path), "worker", "name",
            TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        Assert.NotNull(claim);
        Assert.Equal(3, server.Writes);

        using var other = new Server();
        using var otherDirectory = BoundDirectory(other);
        other.BeforeNextPut = () =>
        {
            var changed = other.Root;
            changed["TaskProposals"] = new JsonObject
            {
                [Id] = JsonNode.Parse("""
                    {"ReportId":"BSR-1-0000000000000000001","ProjectId":1,"State":"ready",
                     "Relevance":"current","RecommendedAction":"create_task","Title":"Ready",
                     "Description":"Description","AcceptanceCriteria":"Criteria",
                     "CreatedAt":"2025-01-01T00:00:00.000Z","UpdatedAt":"2025-01-01T00:00:00.000Z"}
                    """)
            };
            other.Root = changed;
        };
        var noClaim = await Repository(other).ClaimNextEligibleNoteAsync(Binding(otherDirectory.Path), "worker", "name",
            TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        Assert.Null(noClaim);
        Assert.Equal("ready", other.Root["TaskProposals"]![Id]!["State"]!.GetValue<string>());
    }

    private static ProjectBinding Binding(string path) => new(1, path, true);

    private static TempDirectory BoundDirectory(Server server)
    {
        var directory = new TempDirectory();
        var root = server.Root;
        root["Notes"]![Id]!["OriginalPath"] = directory.Path;
        root["Notes"]![Id]!["CreatedAt"] = "2025-01-01T00:00:00.000Z";
        server.Root = root;
        return directory;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "boy-scout-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static async Task<FirebaseTaskProposal> SaveRejection(FirebaseTaskRepository repo, Server server)
    {
        SetProcessingProposal(server);
        var handle = await Acquire(server);
        return await repo.SaveRejectionRecommendationAsync(handle, Id, "Independently verified obsolete",
            "Checked upstream and downstream references.", TestContext.Current.CancellationToken);
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

    private static void SetReadyProposal(Server server, string state)
    {
        var proposal = JsonNode.Parse("""
            {"ReportId":"BSR-1-0000000000000000001","ProjectId":1,"State":"ready",
             "Relevance":"current","RecommendedAction":"create_task","Title":"Title",
             "Description":"Description","AcceptanceCriteria":"Criteria","VerificationSummary":"Verified",
             "CurrentConsequences":"Context","FinalRejectionReason":"Final reason","Error":"Error",
             "CreatedAt":"2025-01-01T00:00:00.000Z","UpdatedAt":"2025-01-02T00:00:00.000Z",
             "AgentCheckedAt":"agent","HumanApprovedAt":"approved","HumanRejectedAt":"rejected","Attempts":1}
            """)!.AsObject();
        if (state is "queued" or "processing")
        {
            foreach (var key in new[] { "Relevance", "RecommendedAction", "Title", "Description", "AcceptanceCriteria", "VerificationSummary", "CurrentConsequences", "FinalRejectionReason", "Error" })
                proposal[key] = null;
        }
        else if (state == "failed")
        {
            proposal["Relevance"] = null;
            proposal["RecommendedAction"] = null;
            proposal["Title"] = null;
            proposal["Description"] = null;
            proposal["AcceptanceCriteria"] = null;
            proposal["VerificationSummary"] = null;
            proposal["FinalRejectionReason"] = null;
        }
        else if (state == "unverified")
        {
            proposal["Relevance"] = "unverified";
            proposal["RecommendedAction"] = null;
            proposal["Title"] = null;
            proposal["Description"] = null;
            proposal["AcceptanceCriteria"] = null;
            proposal["FinalRejectionReason"] = null;
            proposal["Error"] = null;
        }
        else if (state == "rejected")
        {
            proposal["Relevance"] = "obsolete";
            proposal["RecommendedAction"] = "reject";
            proposal["Title"] = null;
            proposal["Description"] = null;
            proposal["AcceptanceCriteria"] = null;
            proposal["CurrentConsequences"] = null;
            proposal["Error"] = null;
        }
        else if (state == "confirmed")
        {
            proposal["Error"] = null;
        }
        proposal["State"] = state;
        var root = server.Root;
        root["TaskProposals"] = new JsonObject
        {
            [Id] = proposal
        };
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
        public int ConflictWriteNumber { get; set; }
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
                if (ConflictNext || Writes == ConflictWriteNumber && ConflictWriteNumber != 0)
                {
                    ConflictNext = false;
                    version++;
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }
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
