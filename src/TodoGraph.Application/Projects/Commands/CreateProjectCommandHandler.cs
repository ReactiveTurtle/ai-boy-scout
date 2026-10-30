using TodoGraph.Domain.Projects;

namespace TodoGraph.Application.Projects.Commands;

public sealed class CreateProjectCommandHandler(IProjectRepository projects)
{
    public async Task<ProjectDto> Handle(CreateProjectCommand command, CancellationToken cancellationToken)
    {
        var project = Project.Create(command.Name, command.FullPath);
        await projects.AddAsync(project, cancellationToken);
        return ProjectDto.From(project);
    }
}
