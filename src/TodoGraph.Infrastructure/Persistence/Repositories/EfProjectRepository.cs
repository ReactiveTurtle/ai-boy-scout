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
}
