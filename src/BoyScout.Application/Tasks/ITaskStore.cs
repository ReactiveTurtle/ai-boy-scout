using BoyScout.Application.Reports;
using BoyScout.Domain.Reports;
using BoyScout.Domain.Tasks;

namespace BoyScout.Application.Tasks;

public interface ITaskStore
{
    TaskProposal? TryClaimNextProposal();
    TaskProposal SaveProposal(
        long id, string title, string description, string acceptanceCriteria,
        string? verificationSummary = null, string? localizedReportTitle = null,
        string? localizedReportObservation = null, string? localizedReportImpact = null,
        string? localizedReportSuggestedAction = null, string? currentConsequences = null);
    TaskProposal FailProposal(long id, string error);
    TaskProposal MarkUnverified(
        long id, string summary, string relevance = TaskProposalRelevances.Unverified,
        string? localizedReportTitle = null, string? localizedReportObservation = null,
        string? localizedReportImpact = null, string? localizedReportSuggestedAction = null);
    TaskProposal SaveRejectionRecommendation(
        long id, string finalRejectionReason, string verificationSummary,
        string? localizedReportTitle = null, string? localizedReportObservation = null,
        string? localizedReportImpact = null, string? localizedReportSuggestedAction = null);
    TaskProposal RetryProposal(long id, string comment);
    TaskProposal RequeueProcessingProposal(long id, string comment);
    TaskProposal RequestRejectionReview(string reportId, string reason);
    TaskProposal RequestClarification(string reportId, string instruction);
    IReadOnlyList<TaskProposal> ListReadyProposals();
    IReadOnlyList<TaskProposal> ListActiveProposals();
    TaskProposal? GetProcessingProposal();
    TaskProposal? GetCurrentProposal();
    BoyScoutTask ConfirmProposal(long id);
    TaskProposal ConfirmRejection(long id);
    IReadOnlyList<BoyScoutTask> ListTasks(long? projectId = null, string? status = null);
    TaskPage BrowseTasks(long? projectId, string? status, string? query, long? before, int limit);
    NoteWorkflowInfo? GetNoteWorkflow(string reportId);
    IReadOnlyList<NoteWorkflowInfo> GetNoteWorkflows(IReadOnlyList<string> reportIds);
    TaskProposal? GetProposalForReport(string reportId);
    RestoredNote RestoreRejectedNote(string reportId, string comment);
    ReprocessedNote ReprocessCompletedNote(string reportId, string comment);
    BoyScoutTask? UpdateStatus(string id, string status);
}
