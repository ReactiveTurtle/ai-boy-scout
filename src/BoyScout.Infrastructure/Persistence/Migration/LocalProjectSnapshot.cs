namespace BoyScout.Infrastructure.Persistence.Migration;

// IDs and timestamp strings are the unmodified SQLite values, not Firebase identifiers.
public sealed record LocalProjectRow(long Id, string Name, string CreatedAt, string UpdatedAt);
public sealed record LocalProjectPathRow(long Id, long ProjectId, string Path, long IsPrimary, string CreatedAt);
public sealed record LocalReportRow(
    long Sequence, string Project, string Title, string? Location, string Observation,
    string? Impact, string? SuggestedAction, string? DiscoveredDuring, string Status,
    string CreatedAt, string UpdatedAt, long? ProjectId, string? SourceName, string? SourceInstallationId);
public sealed record LocalProposalRow(
    long Id, long ReportSequence, long ProjectId, string State, string ReviewIntent,
    string? RequestedRejectionReason, string? RequestedClarification, string? Relevance,
    string? RecommendedAction, string? FinalRejectionReason, string? VerificationSummary,
    string? Title, string? Description, string? AcceptanceCriteria,
    string? LocalizedReportTitle, string? LocalizedReportObservation,
    string? LocalizedReportImpact, string? LocalizedReportSuggestedAction,
    string? Error, long Attempts, string CreatedAt, string UpdatedAt,
    string? AgentCheckedAt, string? HumanApprovedAt, string? HumanRejectedAt,
    string? CurrentConsequences);
public sealed record LocalTaskRow(
    long Sequence, long ProjectId, string Title, string Description, string AcceptanceCriteria,
    string Status, string CreatedAt, string UpdatedAt, string? ConfirmerName, string? ConfirmerInstallationId,
    string? CurrentConsequences);
public sealed record LocalReportTaskRow(long ReportSequence, long TaskSequence);
public sealed record LocalAiConsentRow(long ProjectId, string GrantedAt);
public sealed record LocalSnapshotCounts(int Paths, int Reports, int Proposals, int Tasks, int ReportTaskLinks, int AiConsent);

public sealed record LocalProjectSnapshot(
    LocalProjectRow Project,
    IReadOnlyList<LocalProjectPathRow> Paths,
    IReadOnlyList<LocalReportRow> Reports,
    IReadOnlyList<LocalProposalRow> Proposals,
    IReadOnlyList<LocalTaskRow> Tasks,
    IReadOnlyList<LocalReportTaskRow> ReportTaskLinks,
    LocalAiConsentRow? AiConsent,
    LocalSnapshotCounts Counts,
    string Sha256);
