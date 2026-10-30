using BoyScout.Application.Reports;
using BoyScout.Domain.Projects;

namespace BoyScout.Application.Projects;

public interface IProjectRepository
{
    Task<IReadOnlyList<ProjectSummary>> ListProjectSummariesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BoyScoutProject>> ListProjectsWithPathsAsync(CancellationToken cancellationToken);
    Task<ProjectSummary?> GetProjectSummaryAsync(long projectId, CancellationToken cancellationToken);
    Task<BoyScoutProject?> FindByPathAsync(string normalizedPath, CancellationToken cancellationToken);
    Task<bool> IsPathLinkedAsync(string normalizedPath, CancellationToken cancellationToken);
    Task<bool?> GetAiAccessAsync(long projectId, CancellationToken cancellationToken);
    Task<bool> SetAiAccessAsync(long projectId, bool granted, DateTimeOffset changedAt, CancellationToken cancellationToken);
    void Add(BoyScoutProject project);
}
