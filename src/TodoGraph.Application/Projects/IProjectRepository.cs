using TodoGraph.Domain.Projects;

namespace TodoGraph.Application.Projects;

public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken cancellationToken);
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectTask>?> ListTasksAsync(Guid projectId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
