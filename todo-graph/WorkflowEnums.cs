using System.Text.Json;

namespace TodoGraph;

public enum TaskExecutionStatus
{
    Idle,
    Queued,
    Clarifying,
    AwaitingClarification,
    ReadyForDevelopment,
    Preparing,
    RetryWorktree,
    Prepared,
    Running,
    AwaitingDevelopmentClarification,
    AwaitingCommit,
    Committing,
    AwaitingPush,
    AwaitingUserPr,
    AwaitingMrRepositoryUrl,
    Pushed,
    CreatingMr,
    MrRetryRequired,
    CodeReview,
    ReviewingComments,
    CommentsReviewed,
    Blocked,
    Failed,
    Closed,
}

public enum ProjectBuildStatus
{
    NotStarted,
    Running,
    Passed,
    Failed,
}

public enum MergeRequestStatus
{
    None,
    Open,
    Closed,
    Merged,
}

public enum CodeHost
{
    Gitlab,
    Github,
}

public enum MergeApprovalState
{
    Unknown,
    Approved,
    Awaiting,
    ChangesRequested,
    NotRequired,
}

public enum MergeabilityState
{
    Unknown,
    Mergeable,
    Blocked,
}

public static class WorkflowEnumExtensions
{
    public static string ToStorageString<TEnum>(this TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static TEnum ParseStorageString<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var name = string.Concat(value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return Enum.TryParse<TEnum>(name, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : fallback;
    }
}
