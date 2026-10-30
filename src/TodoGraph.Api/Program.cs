using Microsoft.EntityFrameworkCore;
using TodoGraph.Api.Endpoints;
using TodoGraph.Api.Hubs;
using TodoGraph.Application.Host;
using TodoGraph.Infrastructure;
using TodoGraph.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var databasePath = ResolveDatabasePath();

builder.Services.AddTodoGraphApplication();
builder.Services.AddTodoGraphInfrastructure(databasePath);
builder.Services.AddSignalR();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<TodoGraphDbContext>().Database.MigrateAsync();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapProjectEndpoints();
app.MapHub<ProjectHub>("/hubs/projects");

app.Run();

static string ResolveDatabasePath()
{
    var configured = Environment.GetEnvironmentVariable("TODO_GRAPH_DB");
    if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "todo-graph-v2",
        "todo-graph.db");
}
