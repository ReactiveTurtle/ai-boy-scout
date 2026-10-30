namespace TodoGraph.Application.Projects.Queries;

public sealed class ListProjectTasksQueryHandler(IProjectRepository projects)
{
    public async Task<IReadOnlyList<ProjectTaskDto>?> Handle(
        ListProjectTasksQuery query,
        CancellationToken cancellationToken)
    {
        var tasks = await projects.ListTasksAsync(query.ProjectId, cancellationToken);
        return tasks?.Select(task => ProjectTaskDto.From(query.ProjectId, task)).ToArray();
    }
}
