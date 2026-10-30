using TodoGraph.Domain.Projects;

namespace TodoGraph.Application.Projects;

public sealed record ProjectTaskDto(Guid Id, Guid ProjectId, string? Title, string Description)
{
    public static ProjectTaskDto From(Guid projectId, ProjectTask task) =>
        new(task.Id, projectId, task.Title, task.Description);
}
