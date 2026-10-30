namespace TodoGraph.Application.Projects.Commands;

public sealed class CreateProjectTaskCommandHandler(IProjectRepository projects)
{
    public async Task<ProjectTaskDto?> Handle(CreateProjectTaskCommand command, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(command.ProjectId, cancellationToken);
        if (project is null) return null;

        var task = project.CreateTask(command.Description);
        await projects.SaveChangesAsync(cancellationToken);
        return ProjectTaskDto.From(project.Id, task);
    }
}
