namespace BoyScout.Core.Reports;

public interface IReportStore
{
    ReportPage Browse(long? projectId, string? status, string? search, long? before, int limit);
    ReportPage BrowseNotes(long? projectId, string? noteStatus, string? reviewState, string? search, long? before, int limit);
    IReadOnlyList<ProjectSummary> ListProjects();
    ProjectSummary CreateProject(string name, string path);
    bool? GetAiAccess(long projectId);
    bool SetAiAccess(long projectId, bool granted);
    BoyScoutReport Add(CreateReport request);
    IReadOnlyList<BoyScoutReport> List(string? project, string? status, int limit);
    BoyScoutReport? Get(string id);
    BoyScoutReport? UpdateStatus(string id, string status);
}
