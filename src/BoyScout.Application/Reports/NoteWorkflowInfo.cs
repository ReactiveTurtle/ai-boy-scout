namespace BoyScout.Application.Reports;

public sealed record NoteWorkflowInfo(
    string ReportId,
    string NoteState,
    string ReviewState,
    string? Relevance,
    string? RecommendedAction,
    string? FinalRejectionReason,
    string? Reason,
    string? SubmittedRejectionReason,
    string? AgentVerification,
    string UpdatedAt,
    string? AgentCheckedAt = null,
    string? HumanApprovedAt = null,
    string? HumanRejectedAt = null);
