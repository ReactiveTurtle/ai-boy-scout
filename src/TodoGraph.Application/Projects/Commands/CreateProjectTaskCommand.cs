namespace TodoGraph.Application.Projects.Commands;

public sealed record CreateProjectTaskCommand(Guid ProjectId, string Description);
