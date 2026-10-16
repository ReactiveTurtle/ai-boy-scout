using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoyScout.Api.Ai;
using BoyScout.Core.Reports;
using BoyScout.Core.Storage;
using BoyScout.Core.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class OpenCodeProposalGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_titles_and_keeps_its_session()
    {
        var runner = new FakeCommandRunner(Completed(ValidOutput("ses_owned")));
        var generated = await CreateGenerator(runner).GenerateAsync(NewProposal(), CancellationToken.None);

        Assert.Equal(TaskProposalRelevances.Current, generated.Relevance);
        Assert.Equal("Пользователи теряют несохраненные данные.", generated.CurrentConsequences);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("fake-opencode", call.FileName);
        Assert.Equal("test-config.json", call.ConfigPath);
        Assert.Equal(Path.GetFullPath("."), call.WorkingDirectory);
        Assert.Equal(TimeSpan.FromMinutes(3), call.Timeout);
        Assert.Equal("run", call.Arguments[0]);
        Assert.Equal("--title", call.Arguments[1]);
        Assert.Equal("BoyScout: verification BSR-000001", call.Arguments[2]);
        Assert.DoesNotContain("delete", call.Arguments);
    }

    [Fact]
    public async Task GenerateAsync_accepts_valid_output_without_a_session_id()
    {
        var output = JsonSerializer.Serialize(new { type = "text", part = new { text = ValidResponse } });
        var runner = new FakeCommandRunner(Completed(output));

        Assert.Equal(TaskProposalRelevances.Current,
            (await CreateGenerator(runner).GenerateAsync(NewProposal(), CancellationToken.None)).Relevance);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task GenerateAsync_preserves_invalid_response_without_deleting_session()
    {
        var runner = new FakeCommandRunner(Completed(
            JsonSerializer.Serialize(new { type = "text", sessionID = "ses_invalid", part = new { text = "{}" } })));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateGenerator(runner).GenerateAsync(NewProposal(), CancellationToken.None));

        Assert.Contains("invalid response", exception.Message, StringComparison.Ordinal);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task GenerateAsync_preserves_nonzero_exit_without_deleting_session()
    {
        var runner = new FakeCommandRunner(new OpenCodeCommandResult(23, SessionEvent("ses_failed"), "run failed"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateGenerator(runner).GenerateAsync(NewProposal(), CancellationToken.None));

        Assert.Contains("exited with code 23", exception.Message, StringComparison.Ordinal);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task GenerateAsync_preserves_cancellation_and_timeout_without_deleting_session()
    {
        var cancelled = new FakeCommandRunner(new OpenCodeCommandResult(-1, SessionEvent("ses_cancelled"),
            string.Empty, Cancelled: true));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateGenerator(cancelled).GenerateAsync(NewProposal(), cancellation.Token));
        Assert.Single(cancelled.Calls);

        var timedOut = new FakeCommandRunner(new OpenCodeCommandResult(-1, SessionEvent("ses_timeout"),
            "slow", TimedOut: true));
        await Assert.ThrowsAsync<TimeoutException>(
            () => CreateGenerator(timedOut).GenerateAsync(NewProposal(), CancellationToken.None));
        Assert.Single(timedOut.Calls);
    }

    [Theory]
    [InlineData("obsolete", "reject", "Указанный дефект устранен.")]
    [InlineData("unverified", null, null)]
    public void ParseResponse_requires_null_consequences_for_noncurrent_verdicts(
        string relevance, string? action, string? reason)
    {
        var response = JsonNode.Parse(ValidResponse)!.AsObject();
        response["relevance"] = relevance;
        response["recommendedAction"] = action;
        response["finalRejectionReason"] = reason;
        response["title"] = null;
        response["description"] = null;
        response["acceptanceCriteria"] = null;
        response["currentConsequences"] = null;

        Assert.Null(OpenCodeProposalGenerator.ParseResponse(response.ToJsonString()).CurrentConsequences);

        response["currentConsequences"] = "Текущие последствия";
        Assert.Throws<InvalidOperationException>(
            () => OpenCodeProposalGenerator.ParseResponse(response.ToJsonString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"   \"")]
    public void ParseResponse_rejects_missing_or_invalid_current_consequences(string? replacement)
    {
        var response = JsonNode.Parse(ValidResponse)!.AsObject();
        if (replacement is null)
        {
            response.Remove("currentConsequences");
        }
        else
        {
            response["currentConsequences"] = JsonNode.Parse(replacement);
        }

        Assert.Throws<InvalidOperationException>(
            () => OpenCodeProposalGenerator.ParseResponse(response.ToJsonString()));
    }

    [Fact]
    public void CompleteProposal_persists_generated_consequences_separately_from_source_impact()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boy-scout-consequences-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = new BoyScoutDatabase(Path.Combine(directory, "reports.db"));
            database.Migrate();
            var reports = new ReportStore(database);
            var report = reports.Add(new CreateReport(
                directory, "Finding", null, "Observation", "Raw impact", null, null));
            Assert.True(reports.SetAiAccess(Assert.Single(reports.ListProjects()).Id, true));
            var store = new TaskStore(database);
            var proposal = store.TryClaimNextProposal()!;
            var response = JsonNode.Parse(ValidResponse)!.AsObject();
            response["localizedReportImpact"] = "Перевод исходного влияния.";
            var generated = OpenCodeProposalGenerator.ParseResponse(response.ToJsonString());

            var ready = ProposalGenerationService.CompleteProposal(store, proposal, generated);

            Assert.Equal("Пользователи теряют несохраненные данные.", ready.CurrentConsequences);
            Assert.Equal("Перевод исходного влияния.", ready.LocalizedReportImpact);
            Assert.Equal(ready.CurrentConsequences, store.GetProposalForReport(report.Id)!.CurrentConsequences);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static OpenCodeProposalGenerator CreateGenerator(FakeCommandRunner runner) =>
        new(
            new OpenCodeOptions(
                true,
                TimeSpan.FromMinutes(3),
                "fake-opencode",
                "test-config.json",
                Path.GetFullPath(".")),
            runner);

    private static OpenCodeCommandResult Completed(string output) =>
        new(0, output, string.Empty);

    private static string SessionEvent(string sessionId) =>
        JsonSerializer.Serialize(new { type = "step_start", sessionID = sessionId, part = new { } });

    private static string ValidOutput(string sessionId) =>
        JsonSerializer.Serialize(
            new { type = "text", sessionID = sessionId, part = new { text = ValidResponse } });

    private const string ValidResponse = """
        {
          "relevance": "current",
          "recommendedAction": "create_task",
          "finalRejectionReason": null,
          "verificationSummary": "Проблема подтверждена.",
          "title": "Исправить проблему",
          "description": "Устранить подтвержденную проблему.",
          "acceptanceCriteria": "Проблема устранена.",
          "currentConsequences": "Пользователи теряют несохраненные данные.",
          "localizedReportTitle": "Проблема",
          "localizedReportObservation": "Наблюдаемое поведение.",
          "localizedReportImpact": null,
          "localizedReportSuggestedAction": null
        }
        """;

    private static TaskProposal NewProposal() =>
        new(
            Id: 1,
            ReportId: "BSR-000001",
            ProjectId: 1,
            ReportProject: Path.Combine(Path.GetFullPath("."), "missing-project"),
            State: TaskProposalStates.Processing,
            ReviewIntent: TaskProposalReviewIntents.Proposal,
            RequestedRejectionReason: null,
            RequestedClarification: null,
            Relevance: null,
            RecommendedAction: null,
            FinalRejectionReason: null,
            VerificationSummary: null,
            Title: null,
            Description: null,
            AcceptanceCriteria: null,
            Error: null,
            Attempts: 1,
            CreatedAt: "2026-10-01T00:00:00Z",
            UpdatedAt: "2026-10-01T00:00:00Z",
            ReportTitle: "Finding",
            ReportLocation: null,
            ReportObservation: "Observation",
            ReportImpact: null,
            ReportSuggestedAction: null,
            ReportDiscoveredDuring: null,
            LocalizedReportTitle: null,
            LocalizedReportObservation: null,
            LocalizedReportImpact: null,
            LocalizedReportSuggestedAction: null);

    private sealed class FakeCommandRunner(params OpenCodeCommandResult[] results)
        : IOpenCodeCommandRunner
    {
        private readonly Queue<OpenCodeCommandResult> results = new(results);

        public List<CommandCall> Calls { get; } = [];

        public Task<OpenCodeCommandResult> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(
                new CommandCall(
                    startInfo.FileName,
                    startInfo.ArgumentList.ToArray(),
                    startInfo.WorkingDirectory,
                    startInfo.Environment["OPENCODE_CONFIG"],
                    timeout,
                    cancellationToken));
            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed record CommandCall(
        string FileName,
        string[] Arguments,
        string WorkingDirectory,
        string? ConfigPath,
        TimeSpan Timeout,
        CancellationToken CancellationToken);
}
