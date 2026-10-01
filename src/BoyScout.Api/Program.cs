using BoyScout.Api.Ai;
using BoyScout.Core.Automation;
using BoyScout.Core.Reports;
using BoyScout.Core.Storage;
using BoyScout.Core.Tasks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var database = new BoyScoutDatabase(BoyScoutDatabasePath.Resolve());
database.Migrate();
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<ReportStore>();
builder.Services.AddSingleton<TaskStore>();
builder.Services.AddSingleton<AutomationStore>();

var aiOptions = OpenCodeOptions.FromEnvironment();
builder.Services.AddSingleton(aiOptions);
builder.Services.AddSingleton<AiProcessingControl>();
if (aiOptions.Enabled)
{
    builder.Services.AddSingleton<IOpenCodeCommandRunner, OpenCodeCommandRunner>();
    builder.Services.AddSingleton<OpenCodeProposalGenerator>();
    builder.Services.AddHostedService<ProposalGenerationService>();
}

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/automation", (AiProcessingControl control) => Results.Ok(control.Status));
app.MapPatch(
    "/api/automation",
    IResult (AiProcessingControl control, AutomationUpdate request) =>
    {
        try
        {
            return Results.Ok(control.SetEnabled(request.Enabled));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    });
app.MapGet("/api/projects", (ReportStore store) => Results.Ok(store.ListProjects()));
app.MapPatch(
    "/api/projects/{id:long}/ai-access",
    IResult (ReportStore store, AiProcessingControl control, long id, ProjectAiAccessUpdate request) =>
    {
        if (!store.SetAiAccess(id, request.Granted))
        {
            return Results.NotFound();
        }

        if (!request.Granted)
        {
            control.CancelCurrentGeneration();
        }

        return Results.Ok(new { projectId = id, granted = request.Granted });
    });
app.MapGet(
    "/api/reports",
    IResult (
        ReportStore store,
        long? projectId,
        string? status,
        string? query,
        long? before,
        int limit = 40) =>
    {
        try
        {
            return Results.Ok(store.Browse(projectId, status, query, before, limit));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapGet(
    "/api/reports/{id}",
    (ReportStore store, string id) =>
    {
        try
        {
            var report = store.Get(id);
            return report is null ? Results.NotFound() : Results.Ok(report);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapGet(
    "/api/notes",
    IResult (
        ReportStore store,
        long? projectId,
        string? noteStatus,
        string? reviewState,
        string? query,
        long? before,
        int limit = 40) =>
    {
        try
        {
            return Results.Ok(store.BrowseNotes(projectId, noteStatus, reviewState, query, before, limit));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapGet(
    "/api/notes/{id}/workflow",
    IResult (TaskStore store, string id) =>
    {
        try
        {
            var workflow = store.GetNoteWorkflow(id);
            return workflow is null ? Results.NotFound() : Results.Ok(workflow);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPost(
    "/api/notes/workflows",
    IResult (TaskStore store, NoteWorkflowsRequest request) =>
    {
        try
        {
            if (request.Ids is null)
            {
                return Results.BadRequest(new { error = "The ids array is required." });
            }

            return Results.Ok(store.GetNoteWorkflows(request.Ids));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapGet(
    "/api/notes/{id}/proposal",
    IResult (TaskStore store, string id) =>
    {
        try
        {
            var proposal = store.GetProposalForReport(id);
            return proposal is null ? Results.NoContent() : Results.Ok(proposal);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPost(
    "/api/notes/{id}/restore",
    IResult (TaskStore store, string id) =>
    {
        try
        {
            return Results.Ok(store.RestoreRejectedNote(id));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPost(
    "/api/notes/{id}/reprocess",
    IResult (TaskStore store, string id) =>
    {
        try
        {
            return Results.Ok(store.ReprocessCompletedNote(id));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPost(
    "/api/notes/{id}/rejection-review",
    IResult (TaskStore store, string id, RejectionReviewRequest request) =>
    {
        try
        {
            return Results.Ok(store.RequestRejectionReview(id, request.Reason ?? string.Empty));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPost(
    "/api/notes/{id}/clarification",
    IResult (TaskStore store, string id, ClarificationRequest request) =>
    {
        try
        {
            return Results.Ok(store.RequestClarification(id, request.Instruction ?? string.Empty));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapGet(
    "/api/task-proposals/current",
    (TaskStore store) =>
    {
        var proposal = store.GetCurrentProposal();
        return proposal is null ? Results.NoContent() : Results.Ok(proposal);
    });
app.MapPost(
    "/api/task-proposals/{id:long}/confirm",
    IResult (TaskStore store, long id) =>
    {
        try
        {
            return Results.Ok(store.ConfirmProposal(id));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPost(
    "/api/task-proposals/{id:long}/confirm-rejection",
    IResult (TaskStore store, long id) =>
    {
        try
        {
            return Results.Ok(store.ConfirmRejection(id));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPost(
    "/api/task-proposals/{id:long}/retry",
    IResult (TaskStore store, long id) =>
    {
        try
        {
            return Results.Ok(store.RetryProposal(id));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPost(
    "/api/task-proposals/{id:long}/requeue",
    IResult (TaskStore store, AiProcessingControl control, long id) =>
    {
        try
        {
            var proposal = store.RequeueProcessingProposal(id);
            control.CancelCurrentGeneration();
            return Results.Ok(proposal);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapGet(
    "/api/tasks",
    IResult (TaskStore store, long? projectId, string? status) =>
    {
        try
        {
            return Results.Ok(store.ListTasks(projectId, status));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPatch(
    "/api/tasks/{id}/status",
    IResult (TaskStore store, string id, TaskStatusUpdate request) =>
    {
        try
        {
            var task = store.UpdateStatus(id, request.Status ?? string.Empty);
            return task is null ? Results.NotFound() : Results.Ok(task);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });

app.MapFallbackToFile("index.html");
app.Run();

internal sealed record TaskStatusUpdate(string? Status);

internal sealed record AutomationUpdate(bool Enabled);

internal sealed record ProjectAiAccessUpdate(bool Granted);

internal sealed record RejectionReviewRequest(string? Reason);

internal sealed record ClarificationRequest(string? Instruction);

internal sealed record NoteWorkflowsRequest(IReadOnlyList<string>? Ids);
