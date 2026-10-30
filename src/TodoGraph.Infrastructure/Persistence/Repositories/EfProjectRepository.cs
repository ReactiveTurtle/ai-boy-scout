using Microsoft.EntityFrameworkCore;
using TodoGraph.Application.Projects;
using TodoGraph.Domain.Projects;

namespace TodoGraph.Infrastructure.Persistence.Repositories;

public sealed class EfProjectRepository(TodoGraphDbContext context) : IProjectRepository
{
    public async Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        context.Projects.Add(project);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken) =>
        await context.Projects.AsNoTracking().OrderBy(project => project.Name).ToArrayAsync(cancellationToken);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Projects.Include(project => project.Tasks).SingleOrDefaultAsync(project => project.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProjectTask>?> ListTasksAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await context.Projects.AsNoTracking()
            .Include(item => item.Tasks)
            .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken);
        return project?.Tasks.ToArray();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
