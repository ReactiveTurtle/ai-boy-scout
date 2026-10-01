using System.Diagnostics;
using System.Text.Json;
using BoyScout.Api.Ai;
using BoyScout.Core.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class OpenCodeProposalGeneratorTests
{
    [Fact]
    public void ExtractSessionId_accepts_supported_property_names_in_any_event_order()
    {
        var output = string.Join(
            Environment.NewLine,
            """{"type":"step_start","part":{}}""",
            """{"type":"tool_use","sessionId":"ses_owned","part":{}}""",
            """{"type":"text","sessionID":"ses_owned","part":{"text":"result"}}""");

        var sessionId = OpenCodeProposalGenerator.ExtractSessionId(output);

        Assert.Equal("ses_owned", sessionId);
    }

    [Fact]
    public void ExtractSessionId_rejects_malformed_json()
    {
        Assert.ThrowsAny<JsonException>(
            () => OpenCodeProposalGenerator.ExtractSessionId("not-json"));
    }

    [Theory]
    [InlineData("{\"sessionID\":42}", typeof(InvalidOperationException))]
    [InlineData(
        "{\"sessionID\":\"ses_one\"}\n{\"sessionId\":\"ses_two\"}",
        typeof(InvalidOperationException))]
    public void ExtractSessionId_rejects_malformed_or_inconsistent_events(
        string output,
        Type exceptionType)
    {
        Assert.Throws(exceptionType, () => OpenCodeProposalGenerator.ExtractSessionId(output));
    }

    [Fact]
    public void ExtractSessionId_ignores_nested_session_properties()
    {
        var sessionId = OpenCodeProposalGenerator.ExtractSessionId(
            """{"type":"text","part":{"sessionID":"ses_nested"}}""");

        Assert.Null(sessionId);
    }

    [Fact]
    public async Task GenerateAsync_deletes_the_emitted_session_after_success()
    {
        var runner = new FakeCommandRunner(
            Completed(ValidOutput("ses_owned")),
            Completed(string.Empty));
        var generator = CreateGenerator(runner);

        var generated = await generator.GenerateAsync(NewProposal(), CancellationToken.None);

        Assert.Equal(TaskProposalRelevances.Current, generated.Relevance);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(
            new[] { "session", "delete", "ses_owned" },
            runner.Calls[1].Arguments);
        Assert.Equal("fake-opencode", runner.Calls[1].FileName);
        Assert.Equal("test-config.json", runner.Calls[1].ConfigPath);
        Assert.Equal(Path.GetFullPath("."), runner.Calls[1].WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(15), runner.Calls[1].Timeout);
        Assert.False(runner.Calls[1].CancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task GenerateAsync_deletes_the_session_after_an_invalid_response()
    {
        var output = JsonSerializer.Serialize(
            new { type = "text", sessionID = "ses_invalid", part = new { text = "{}" } });
        var runner = new FakeCommandRunner(Completed(output), Completed(string.Empty));
        var generator = CreateGenerator(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(NewProposal(), CancellationToken.None));

        Assert.Contains("invalid response", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "session", "delete", "ses_invalid" }, runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task GenerateAsync_preserves_nonzero_exit_when_cleanup_also_fails()
    {
        var runner = new FakeCommandRunner(
            new OpenCodeCommandResult(23, SessionEvent("ses_failed"), "run failed"),
            new OpenCodeCommandResult(7, string.Empty, "delete failed"));
        var generator = CreateGenerator(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(NewProposal(), CancellationToken.None));

        Assert.Contains("exited with code 23", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("code 7", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "session", "delete", "ses_failed" }, runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task GenerateAsync_uses_independent_cleanup_after_external_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new FakeCommandRunner(
            new OpenCodeCommandResult(
                -1,
                SessionEvent("ses_cancelled"),
                string.Empty,
                Cancelled: true),
            Completed(string.Empty));
        var generator = CreateGenerator(runner);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => generator.GenerateAsync(NewProposal(), cancellation.Token));

        Assert.Equal(new[] { "session", "delete", "ses_cancelled" }, runner.Calls[1].Arguments);
        Assert.False(runner.Calls[1].CancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task GenerateAsync_deletes_the_session_after_timeout()
    {
        var runner = new FakeCommandRunner(
            new OpenCodeCommandResult(
                -1,
                SessionEvent("ses_timeout"),
                "slow",
                TimedOut: true),
            Completed(string.Empty));
        var generator = CreateGenerator(runner);

        await Assert.ThrowsAsync<TimeoutException>(
            () => generator.GenerateAsync(NewProposal(), CancellationToken.None));

        Assert.Equal(new[] { "session", "delete", "ses_timeout" }, runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task GenerateAsync_fails_a_successful_run_when_cleanup_fails()
    {
        var runner = new FakeCommandRunner(
            Completed(ValidOutput("ses_leaked")),
            new OpenCodeCommandResult(9, string.Empty, "cannot delete"));
        var generator = CreateGenerator(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(NewProposal(), CancellationToken.None));

        Assert.Contains("session could not be deleted", exception.Message, StringComparison.Ordinal);
        Assert.Contains("code 9", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_never_guesses_a_session_when_the_id_is_missing()
    {
        var output = JsonSerializer.Serialize(
            new { type = "text", part = new { text = ValidResponse } });
        var runner = new FakeCommandRunner(Completed(output));
        var generator = CreateGenerator(runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateAsync(NewProposal(), CancellationToken.None));

        Assert.Contains("session could not be deleted", exception.Message, StringComparison.Ordinal);
        Assert.Single(runner.Calls);
    }

    private static OpenCodeProposalGenerator CreateGenerator(FakeCommandRunner runner) =>
        new(
            new OpenCodeOptions(
                true,
                TimeSpan.FromMinutes(3),
                "fake-opencode",
                "test-config.json",
                Path.GetFullPath(".")),
            runner,
            NullLogger<OpenCodeProposalGenerator>.Instance);

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
