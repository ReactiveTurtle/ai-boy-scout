using TodoGraph.Domain.Processes;

namespace TodoGraph.Application.Processes;

public interface IProcessRunRepository
{
    Task<ProcessRun?> GetForProjectTaskAsync(Guid projectTaskId, CancellationToken cancellationToken);
    Task SaveAsync(Guid projectTaskId, ProcessRun processRun, CancellationToken cancellationToken);
}
