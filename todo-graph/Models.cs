namespace TodoGraph;

public sealed record CandidateImport(
    string SourceReportId,
    string Project,
    string Title,
    string Observation,
    string? Location,
    string? Impact,
    string? SuggestedAction,
    string? DiscoveredDuring,
    string Status,
    string CreatedAt,
    string SourceUrl,
    long? SourceProjectId = null,
    IReadOnlyList<ImportedTask>? SourceTasks = null,
    string? CreatedBy = null);

public sealed record ImportedTask(
    string SourceTaskId,
    long SourceProjectId,
    string Project,
    string Title,
    string Description,
    string AcceptanceCriteria,
    string? CurrentConsequences,
    string Status,
    string CreatedAt,
    string UpdatedAt,
    IReadOnlyList<string> SourceReportIds,
    string? CreatedBy = null);

public sealed record TaskNode(
    long Id,
    long? SourceProjectId,
    string? SourceTaskId,
    string Project,
    string Title,
    string Description,
    string AcceptanceCriteria,
    string? CurrentConsequences,
    string Status,
    IReadOnlyList<string> SourceReportIds,
    bool UserModified,
    string CreatedAt,
    string UpdatedAt,
    string? MergeRequestUrl = null,
    TaskExecutionStatus ExecutionStatus = TaskExecutionStatus.Idle,
    ProjectBuildStatus BuildStatus = ProjectBuildStatus.NotStarted,
    string? RepositoryPath = null,
    string? ExecutionError = null,
    string? WorktreePath = null,
    string? BranchName = null,
    string? BaseBranch = null,
    MergeRequestStatus MergeRequestStatus = MergeRequestStatus.None,
    string? CommitSha = null,
    string? MergeRequestSourceBranch = null,
    string? CreatedBy = null,
    CodeHost CodeHost = CodeHost.Gitlab,
    MergeApprovalState ApprovalState = MergeApprovalState.Unknown,
    MergeabilityState Mergeability = MergeabilityState.Unknown,
    int? ApprovedCount = null,
    int? RequiredApprovals = null,
    string? MergeRequestTargetBranch = null,
    string? RemoteRepositoryUrl = null);

public sealed record Candidate(
    long Id,
    string SourceReportId,
    string Project,
    string Title,
    string Observation,
    string? Location,
    string? Impact,
    string? SuggestedAction,
    string? DiscoveredDuring,
    string Status,
    string CreatedAt,
    string ReceivedAt,
    string SourceUrl,
    string TriageStatus,
    IReadOnlyList<CandidateLink> Links,
    IReadOnlyList<LinkSuggestion> SuggestedLinks,
    string AgentStatus = "queued",
    string? LocalizedTitle = null,
    string? LocalizedDescription = null,
    long? SourceProjectId = null,
    string? CreatedBy = null,
    string Source = "boyscout",
    string? RawText = null,
    IReadOnlyList<CandidateClarificationRound>? ClarificationRounds = null);

public sealed record CandidateClarificationRound(int Round, IReadOnlyList<string> Questions, string? Answer);

public sealed record RawItemSubmissionRequest(string Text);

public sealed record CandidateLink(long CandidateId, string Relation, string CandidateTitle);

public sealed record LinkSuggestion(long CandidateId, string CandidateTitle, string Relation, double Score);

public sealed record TriageDecision(string Status);

public sealed record CandidateLinkRequest(long CandidateId, string Relation);

public sealed record TaskLinkRequest(long RelatedTaskId, string Relation);

public sealed record GraphContextNode(
    long Id,
    string Title,
    string Project,
    string Description,
    string? LocalizedTitle = null,
    string? LocalizedDescription = null,
    string Kind = "candidate",
    long? TaskId = null,
    string? Status = null,
    string? AcceptanceCriteria = null,
    string? CurrentConsequences = null);

public sealed record GraphContextEdge(long FromId, long ToId, string Relation);

public sealed record CandidateGraphContext(
    long CandidateId,
    string SourceReportId,
    string SourceUrl,
    string CandidateTitle,
    string CandidateDescription,
    int AvailableNodeCount,
    IReadOnlyList<GraphContextNode> Nodes,
    IReadOnlyList<GraphContextEdge> Edges,
    IReadOnlyList<GraphExplorationStep> Exploration,
    string? LocalizedCandidateTitle = null,
    string? LocalizedCandidateDescription = null,
    int AvailableTaskCount = 0,
    string? RawText = null,
    IReadOnlyList<CandidateClarificationRound>? ClarificationRounds = null,
    string Source = "boyscout");

public sealed record GraphExplorationStep(string Action, string Request, IReadOnlyList<long> ReturnedNodeIds);

public sealed record GraphExpansion(IReadOnlyList<GraphContextNode> Nodes, IReadOnlyList<GraphContextEdge> Edges);

public sealed record AgentLinkProposal(long CandidateId, string Relation, string Rationale);

public sealed record AgentTaskPlan(
    string Action,
    long? TargetTaskId,
    string Title,
    string Description,
    string AcceptanceCriteria,
    string Rationale,
    long? Id = null,
    string Status = "pending");

public sealed record CandidateAgentReview(
    long Id,
    long CandidateId,
    string Summary,
    CandidateGraphContext Context,
    IReadOnlyList<AgentLinkSuggestion> Suggestions,
    string CreatedAt,
    AgentTaskPlan? TaskPlan = null,
    IReadOnlyList<string>? Questions = null);

public sealed record CandidateAgentJob(long CandidateId, string Status, int Attempts, string? Error, string UpdatedAt);

public sealed record AgentLinkSuggestion(
    long Id,
    long CandidateId,
    string CandidateTitle,
    string Relation,
    string Rationale,
    string Status);

public sealed record AgentSuggestionDecision(bool Accept);

public sealed record AgentTaskPlanDecision(bool Accept);

public sealed record TaskUpdateRequest(string? MergeRequestUrl, MergeRequestStatus? MergeRequestStatus = null);

public sealed record MergeRequestStatusRequest(MergeRequestStatus Status);

public sealed record ProjectRepository(long SourceProjectId, string Project, string? RepositoryPath, string? UpdatedAt, CodeHost CodeHost = CodeHost.Gitlab, string? RemoteRepositoryUrl = null);

public sealed record ProjectRepositoryRequest(string RepositoryPath, CodeHost CodeHost = CodeHost.Gitlab);

public sealed record CreateManualTaskRequest(
    long ProjectId,
    string Title,
    string Description,
    string? AcceptanceCriteria,
    string? CurrentConsequences,
    string? RepositoryPath = null);

public sealed record CreateProjectRequest(string RepositoryPath, CodeHost CodeHost = CodeHost.Gitlab);

public sealed record ProjectRemoteRepositoryUrlRequest(string RemoteRepositoryUrl);

public sealed record LocalMcpServerUpdate(
    bool Enabled,
    string Executable,
    IReadOnlyList<string> Arguments,
    string TokenEnvironmentVariable,
    string? Token,
    bool ClearToken,
    IReadOnlyList<string> Tools);

public sealed record GitLabMcpSettingsUpdate(
    string DisplayName,
    LocalMcpServerUpdate Read,
    LocalMcpServerUpdate Write,
    LocalMcpServerUpdate? GitHubRead = null);

public sealed record LocalMcpServerView(
    bool Enabled,
    string Executable,
    IReadOnlyList<string> Arguments,
    string TokenEnvironmentVariable,
    bool HasToken,
    IReadOnlyList<string> Tools);

public sealed record GitLabMcpSettingsView(
    string DisplayName,
    LocalMcpServerView Read,
    LocalMcpServerView Write,
    LocalMcpServerView GitHubRead);

public sealed record TaskPreflight(
    long TaskId,
    string Problem,
    string ExpectedResult,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> CornerCases,
    IReadOnlyList<string> Questions,
    string? Decomposition,
    string? Answers,
    string CreatedAt,
    string? BuildCommand = null,
    string? BaseBranch = null,
    bool QuestionsPending = false);

public sealed record TaskPreflightResult(
    string Problem,
    string ExpectedResult,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> CornerCases,
    IReadOnlyList<string> Questions,
    string? Decomposition,
    string? BuildCommand = null,
    string? BaseBranch = null);

public sealed record TaskClarificationRequest(string Answers);

public sealed record DescriptionUpdateRequest(string Description);

public sealed record TaskHistoryEvent(long Id, long TaskId, string EventType, string? PreviousValue, string? NewValue, string OccurredAt);

public sealed record TaskFileMetadata(string Path, long Size, DateTime LastModifiedUtc);

public sealed record MarkMergeRequestCommentReviewedRequest(string CommentId);

public sealed record SaveMergeRequestReplyDraftRequest(string CommentId, string Body);

public sealed record ConfirmMergeRequestReplyRequest(string CommentId);

public sealed record MergeRequestCommentInput(
    string Id,
    string DiscussionId,
    string Body,
    string Author,
    string CreatedAt,
    string? Url);

public sealed record MergeRequestPollResult(
    MergeRequestStatus Status,
    string SourceBranch,
    IReadOnlyList<MergeRequestCommentInput> Comments,
    MergeApprovalState ApprovalState = MergeApprovalState.Unknown,
    MergeabilityState Mergeability = MergeabilityState.Unknown,
    int? ApprovedCount = null,
    int? RequiredApprovals = null,
    string? MergeRequestTargetBranch = null,
    string? RemoteRepositoryUrl = null);

public sealed record MergeRequestComment(
    long TaskId,
    string MergeRequestUrl,
    string Id,
    string DiscussionId,
    string Body,
    string Author,
    string CreatedAt,
    string? Url,
    string ObservedAt,
    string? ReviewedAt = null,
    string? ReplyBody = null,
    string? ReplyStatus = null,
    string? ReplyError = null,
    string? ReplyExternalId = null,
    string? ReplySentAt = null,
    string? AgentHandledAt = null);

public sealed record MergeRequestReply(
    long TaskId,
    string MergeRequestUrl,
    string CommentId,
    string DiscussionId,
    string CommentBody,
    string ReplyBody,
    string Project,
    string TaskTitle);

public sealed record GraphNodeSnapshot(
    string Key,
    string Kind,
    long Id,
    string Title,
    string Project,
    string Description,
    string Status,
    string? SourceReportId,
    string? SourceTaskId = null,
    string? AcceptanceCriteria = null,
    string? CurrentConsequences = null,
    string? MergeRequestUrl = null,
    TaskExecutionStatus? ExecutionStatus = null,
    ProjectBuildStatus? BuildStatus = null,
    long? SourceProjectId = null,
    string? RepositoryPath = null,
    string? ExecutionError = null,
    string? WorktreePath = null,
    string? BranchName = null,
    string? BaseBranch = null,
    MergeRequestStatus? MergeRequestStatus = null,
    bool WorktreeReady = false,
    string? CommitSha = null,
    string? MergeRequestSourceBranch = null,
    string? CreatedBy = null,
    CodeHost? CodeHost = null,
    MergeApprovalState? ApprovalState = null,
    MergeabilityState? Mergeability = null,
    int? ApprovedCount = null,
    int? RequiredApprovals = null,
    string? MergeRequestTargetBranch = null,
    string? RemoteRepositoryUrl = null,
    int CommentCount = 0,
    string? Source = null);

public sealed record GraphEdgeSnapshot(string From, string To, string Relation);

public sealed record GraphSnapshot(IReadOnlyList<GraphNodeSnapshot> Nodes, IReadOnlyList<GraphEdgeSnapshot> Edges);
