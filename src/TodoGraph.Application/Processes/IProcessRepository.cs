using TodoGraph.Domain.Processes;

namespace TodoGraph.Application.Processes;

public interface IProcessRepository
{
    Task AddAsync(Process process, CancellationToken cancellationToken);
    Task<IReadOnlyList<Process>> ListAsync(CancellationToken cancellationToken);
    Task<Process?> GetByIdAsync(Guid processId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
