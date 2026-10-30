using TodoGraph.Domain.Projects;

namespace TodoGraph.Application.Projects;

public sealed record ProjectDto(Guid Id, string Name, string FullPath)
{
    public static ProjectDto From(Project project) => new(project.Id, project.Name, project.FullPath);
}
