using Microsoft.EntityFrameworkCore;
using TodoGraph.Application.Processes;
using TodoGraph.Domain.Processes;

namespace TodoGraph.Infrastructure.Persistence.Repositories;

public sealed class EfProcessRepository(TodoGraphDbContext context) : IProcessRepository
{
    public async Task AddAsync(Process process, CancellationToken cancellationToken)
    {
        context.Processes.Add(process);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Process>> ListAsync(CancellationToken cancellationToken)
    {
        var processes = await context.Processes.AsNoTracking()
            .Include(process => process.Blocks)
            .ThenInclude(block => block.Stages)
            .OrderBy(process => process.Name)
            .ToArrayAsync(cancellationToken);
        foreach (var process in processes) RestoreOrder(process);
        return processes;
    }

    public async Task<Process?> GetByIdAsync(Guid processId, CancellationToken cancellationToken)
    {
        var process = await context.Processes
            .Include(item => item.Blocks)
            .ThenInclude(block => block.Stages)
            .SingleOrDefaultAsync(item => item.Id == processId, cancellationToken);
        if (process is not null) RestoreOrder(process);
        return process;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static void RestoreOrder(Process process)
    {
        foreach (var block in process.Blocks)
        {
            block.ReorderStages(block.Stages.OrderBy(stage => stage.Position).Select(stage => stage.Id).ToArray());
        }

        process.ReorderBlocks(process.Blocks.OrderBy(block => block.Position).Select(block => block.Id).ToArray());
    }
}
