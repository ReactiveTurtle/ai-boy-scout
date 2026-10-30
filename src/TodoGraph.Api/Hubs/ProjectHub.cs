using Microsoft.AspNetCore.SignalR;

namespace TodoGraph.Api.Hubs;

public sealed class ProjectHub : Hub
{
    public const string ProjectCreatedEvent = "ProjectCreated";
}
