namespace TodoGraph.Application.Projects.Queries;

public sealed class ListProjectsQueryHandler(IProjectRepository projects)
{
    public async Task<IReadOnlyList<ProjectDto>> Handle(ListProjectsQuery query, CancellationToken cancellationToken)
    {
        var items = await projects.ListAsync(cancellationToken);
        return items.Select(ProjectDto.From).ToArray();
    }
}
