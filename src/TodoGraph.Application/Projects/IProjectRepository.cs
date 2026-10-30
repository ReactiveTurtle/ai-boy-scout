using TodoGraph.Domain.Projects;

namespace TodoGraph.Application.Projects;

public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken cancellationToken);
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);
}
