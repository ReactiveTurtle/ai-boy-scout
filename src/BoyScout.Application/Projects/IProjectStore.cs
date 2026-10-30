using BoyScout.Application.Reports;

namespace BoyScout.Application.Projects;

public interface IProjectStore
{
    Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken);
    Task<ProjectSummary> ResolveOrCreateProjectAsync(string name, string path, CancellationToken cancellationToken);
    Task<ProjectSummary> CreateProjectAsync(string name, string path, CancellationToken cancellationToken);
    Task<bool?> GetAiAccessAsync(long projectId, CancellationToken cancellationToken);
    Task<bool> SetAiAccessAsync(long projectId, bool granted, CancellationToken cancellationToken);
}
