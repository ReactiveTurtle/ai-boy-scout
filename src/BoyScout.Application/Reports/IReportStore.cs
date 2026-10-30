using BoyScout.Domain.Reports;

namespace BoyScout.Application.Reports;

public interface IReportStore
{
    ReportPage Browse(long? projectId, string? status, string? search, long? before, int limit);
    ReportPage BrowseNotes(long? projectId, string? noteStatus, string? reviewState, string? search, long? before, int limit);
    BoyScoutReport Add(CreateReport request);
    IReadOnlyList<BoyScoutReport> List(string? project, string? status, int limit);
    BoyScoutReport? Get(string id);
    BoyScoutReport? UpdateStatus(string id, string status);
    BoyScoutReport? MarkTransferredToFirebase(string localNoteId, string firebaseNoteId);
}
