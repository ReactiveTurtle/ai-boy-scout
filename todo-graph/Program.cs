using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using TodoGraph;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = ResolveContentRoot(),
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));
var databasePath = Environment.GetEnvironmentVariable("TODO_GRAPH_DB") ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "todo-graph", "todo-graph.db");
var dataProtectionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "todo-graph", "keys");
Directory.CreateDirectory(dataProtectionPath);
var protection = builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
builder.Services.AddSingleton(new CandidateStore(databasePath));
builder.Services.AddSingleton<GitLabMcpSettingsStore>(services => new(databasePath, services.GetRequiredService<IDataProtectionProvider>()));
builder.Services.AddSingleton<OpenCodeMcpConfiguration>();
builder.Services.AddSingleton<TodoGraphAgent>();
builder.Services.AddHostedService<CandidateAgentWorker>();
builder.Services.AddSingleton<MergeRequestReviewAgent>();
builder.Services.AddSingleton<IMergeRequestCommentSource>(services => services.GetRequiredService<MergeRequestReviewAgent>());
builder.Services.AddHostedService<MergeRequestReviewWorker>();
builder.Services.AddSingleton<MergeRequestReplyAgent>();
builder.Services.AddSingleton<IMergeRequestReplyPublisher>(services => services.GetRequiredService<MergeRequestReplyAgent>());
builder.Services.AddHostedService<MergeRequestReplyWorker>();
builder.Services.AddSingleton<ITaskPreflightSource, TaskPreflightAgent>();
builder.Services.AddHostedService<TaskPreflightWorker>();
builder.Services.AddSingleton<ITaskWorktreePreparer, TaskWorktreePreparer>();
builder.Services.AddHostedService<TaskWorktreeWorker>();
builder.Services.AddSingleton<ITaskImplementationAgent, TaskImplementationAgent>();
builder.Services.AddSingleton<IProjectBuildRunner, ProjectBuildRunner>();
builder.Services.AddHostedService<TaskImplementationWorker>();
builder.Services.AddSingleton<ITaskGitOperations, TaskGitOperations>();
builder.Services.AddHostedService<TaskCommitWorker>();
builder.Services.AddSingleton<IMergeRequestCreator, MergeRequestCreationAgent>();
builder.Services.AddHostedService<MergeRequestCreationWorker>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
var defaultFiles = new DefaultFilesOptions();
defaultFiles.DefaultFileNames.Clear();
defaultFiles.DefaultFileNames.Add("projects.html");
app.UseDefaultFiles(defaultFiles);
app.UseStaticFiles();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/local-directories", (string? path) =>
{
    try { return Results.Ok(LocalDirectoryBrowser.List(path)); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    catch (DirectoryNotFoundException error) { return Results.NotFound(new { error = error.Message }); }
    catch (UnauthorizedAccessException error) { return Results.Json(new { error = error.Message }, statusCode: StatusCodes.Status403Forbidden); }
    catch (IOException error) { return Results.Problem(error.Message, statusCode: StatusCodes.Status500InternalServerError); }
});
app.MapGet("/api/settings/gitlab-mcp", (GitLabMcpSettingsStore store) => Results.Ok(store.GetView()));
app.MapGet("/api/settings/code-host-mcp", (GitLabMcpSettingsStore store) => Results.Ok(store.GetView()));
var saveMcpSettings = (GitLabMcpSettingsStore store, GitLabMcpSettingsUpdate settings) =>
{
    try { return Results.Ok(store.Save(settings)); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
};
app.MapPut("/api/settings/gitlab-mcp", saveMcpSettings);
app.MapPut("/api/settings/code-host-mcp", saveMcpSettings);
app.MapPost("/api/candidates", (CandidateStore store, CandidateImport candidate) =>
{
    try
    {
        var (stored, duplicate) = store.Receive(candidate);
        return duplicate ? Results.Ok(stored) : Results.Created($"/api/candidates/{stored.Id}", stored);
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapGet("/api/candidates", (CandidateStore store, string? status, long? projectId) =>
{
    try { return Results.Ok(store.List(status, projectId)); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapPost("/api/projects/{projectId:long}/raw-items", IResult (CandidateStore store, GitLabMcpSettingsStore settings, long projectId, RawItemSubmissionRequest request) =>
{
    try
    {
        var candidate = store.SubmitRawItem(projectId, request.Text, settings.GetView().DisplayName);
        return candidate is null ? Results.NotFound(new { error = "Project not found." }) : Results.Created($"/api/candidates/{candidate.Id}", candidate);
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapGet("/api/tasks", (CandidateStore store, long? projectId) => Results.Ok(store.ListTaskNodes(projectId)));
app.MapPost("/api/tasks/{id:long}/links", IResult (CandidateStore store, long id, TaskLinkRequest link) =>
{
    if (store.GetTaskNode(id) is null) return Results.NotFound(new { error = $"Task {id} was not found." });
    if (store.GetTaskNode(link.RelatedTaskId) is null)
        return Results.NotFound(new { error = $"Related task {link.RelatedTaskId} was not found." });
    try
    {
        store.AddTaskLink(id, link);
        return Results.Ok(new { taskId = id, relatedTaskId = link.RelatedTaskId, relation = link.Relation });
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapGet("/api/tasks/{id:long}/history", (CandidateStore store, long id) =>
    store.GetTaskNode(id) is null ? Results.NotFound() : Results.Ok(store.ListTaskHistory(id)));
app.MapGet("/api/tasks/{id:long}/files", (CandidateStore store, long id) =>
    store.GetTaskNode(id) is null ? Results.NotFound() : Results.Ok(store.ListTaskFiles(id)));
app.MapPatch("/api/tasks/{id:long}/description", IResult (CandidateStore store, long id, DescriptionUpdateRequest request) =>
{
    try { return store.UpdateTaskDescription(id, request.Description) ? Results.Ok(new { updated = true }) : Results.NotFound(); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapPost("/api/tasks", IResult (CandidateStore store, GitLabMcpSettingsStore settings, CreateManualTaskRequest request) =>
{
    try
    {
        var task = store.CreateManualTask(request, settings.GetView().DisplayName);
        return task is null ? Results.NotFound(new { error = "Project not found." }) : Results.Created($"/api/tasks/{task.Id}", task);
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapGet("/api/projects/repositories", (CandidateStore store) => Results.Ok(store.ListProjectRepositories()));
app.MapPost("/api/projects", IResult (CandidateStore store, CreateProjectRequest request) =>
{
    try { return Results.Created("/api/projects/repositories", store.CreateProject(request.RepositoryPath, request.CodeHost)); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapPut("/api/projects/{id:long}/remote-repository-url", IResult (CandidateStore store, long id, ProjectRemoteRepositoryUrlRequest request) =>
{
    try { return store.SetProjectRemoteRepositoryUrl(id, request.RemoteRepositoryUrl) is { } project ? Results.Ok(project) : Results.NotFound(); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapPut("/api/projects/{id:long}/repository", IResult (CandidateStore store, long id, ProjectRepositoryRequest request) =>
{
    try { return store.SetProjectRepository(id, request.RepositoryPath, request.CodeHost) is { } project ? Results.Ok(project) : Results.NotFound(); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
});
app.MapPost("/api/tasks/{id:long}/queue", IResult (CandidateStore store, long id) =>
{
    var task = store.QueueTask(id, out var error);
    return task is null ? Results.NotFound() : error is not null
        ? Results.Conflict(new { error }) : Results.Accepted($"/api/tasks/{id}", task);
});
app.MapPost("/api/tasks/{id:long}/reject", IResult (CandidateStore store, long id) =>
    store.RejectTask(id) is { } task ? Results.Ok(task) : Results.NotFound());
app.MapGet("/api/tasks/{id:long}/preflight", (CandidateStore store, long id) =>
    store.GetTaskNode(id) is null ? Results.NotFound() : Results.Ok(new { preflight = store.GetTaskPreflight(id) }));
app.MapPut("/api/tasks/{id:long}/preflight/answers", IResult (CandidateStore store, long id, TaskClarificationRequest request) =>
{
    try { return store.SaveTaskClarification(id, request.Answers) is { } preflight ? Results.Ok(preflight) : Results.Conflict(new { error = "Task is not awaiting clarification." }); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapPost("/api/tasks/{id:long}/preflight/approve", IResult (CandidateStore store, long id) =>
{
    var task = store.ApproveTaskPreflight(id, out var error);
    return task is null ? Results.NotFound() : error is not null
        ? Results.Conflict(new { error }) : Results.Ok(task);
});
app.MapPost("/api/tasks/{id:long}/worktree/retry", IResult (CandidateStore store, long id) =>
{
    var task = store.RetryWorktreePreparation(id, out var error);
    return task is null ? Results.NotFound() : error is not null
        ? Results.Conflict(new { error }) : Results.Accepted($"/api/tasks/{id}", task);
});
app.MapPost("/api/tasks/{id:long}/development/resume", IResult (CandidateStore store, long id) =>
{
    var task = store.ResumeImplementation(id, out var error);
    return task is null ? Results.NotFound() : error is not null
        ? Results.Conflict(new { error }) : Results.Accepted($"/api/tasks/{id}", task);
});
app.MapPost("/api/tasks/{id:long}/mr/retry", IResult (CandidateStore store, long id) =>
{
    var task = store.RetryMergeRequestCreation(id, out var error);
    return task is null ? Results.NotFound() : error is not null
        ? Results.Conflict(new { error }) : Results.Accepted($"/api/tasks/{id}", task);
});
app.MapPatch("/api/tasks/{id:long}", (CandidateStore store, long id, TaskUpdateRequest request) =>
{
    try { return store.UpdateTask(id, request) is { } task ? Results.Ok(task) : Results.NotFound(); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapPatch("/api/tasks/{id:long}/mr-status", IResult (CandidateStore store, long id, MergeRequestStatusRequest request) =>
{
    try { return store.UpdateMergeRequestStatus(id, request.Status) is { } task ? Results.Ok(task) : Results.Conflict(new { error = "Task has no active MR URL or is closed." }); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapGet("/api/tasks/{id:long}/mr-comments", (CandidateStore store, long id) =>
    store.GetTaskNode(id) is null ? Results.NotFound() : Results.Ok(store.ListMergeRequestComments(id)));
app.MapPost("/api/tasks/{id:long}/mr-comments/reviewed", IResult (
    CandidateStore store, long id, MarkMergeRequestCommentReviewedRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.CommentId)) return Results.BadRequest(new { error = "CommentId is required." });
    return store.MarkMergeRequestCommentReviewed(id, request.CommentId) is { } comment
        ? Results.Ok(comment)
        : Results.NotFound();
});
app.MapPut("/api/tasks/{id:long}/mr-comments/reply-draft", IResult (
    CandidateStore store, long id, SaveMergeRequestReplyDraftRequest request) =>
{
    try
    {
        return store.SaveMergeRequestReplyDraft(id, request.CommentId, request.Body) is { } comment
            ? Results.Ok(comment)
            : Results.Conflict(new { error = "The comment is not on the current Code Review MR or already has a reply in progress/sent." });
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapPost("/api/tasks/{id:long}/mr-comments/reply-confirmation", IResult (
    CandidateStore store, long id, ConfirmMergeRequestReplyRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.CommentId)) return Results.BadRequest(new { error = "CommentId is required." });
    return store.ConfirmMergeRequestReply(id, request.CommentId)
        ? Results.Accepted($"/api/tasks/{id}/mr-comments", new { status = "queued" })
        : Results.Conflict(new { error = "Save a reply draft for a comment on a current Code Review MR before confirming publication." });
});
app.MapGet("/api/graph", (CandidateStore store) => Results.Ok(store.GetGraphSnapshot()));
app.MapGet("/api/candidates/{id:long}", (CandidateStore store, long id) =>
    store.Get(id) is { } candidate ? Results.Ok(candidate) : Results.NotFound());
app.MapPut("/api/candidates/{id:long}/answers", IResult (CandidateStore store, long id, TaskClarificationRequest request) =>
{
    try { return store.AnswerRawItem(id, request.Answers) is { } candidate ? Results.Accepted($"/api/candidates/{id}", candidate) : Results.Conflict(new { error = "Raw item is not awaiting an answer." }); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapGet("/api/candidates/{id:long}/files", (CandidateStore store, long id) =>
    store.Get(id) is null ? Results.NotFound() : Results.Ok(store.ListCandidateFiles(id)));
app.MapPatch("/api/candidates/{id:long}/description", IResult (CandidateStore store, long id, DescriptionUpdateRequest request) =>
{
    try { return store.UpdateCandidateDescription(id, request.Description) ? Results.Ok(new { updated = true }) : Results.NotFound(); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapGet("/api/agent/candidates/{id:long}", IResult (CandidateStore store, long id) =>
{
    var context = store.BuildGraphContext(id);
    return context is null
        ? Results.NotFound()
        : Results.Ok(new { context, review = store.GetLatestAgentReview(id), job = store.GetAgentJob(id) });
});
app.MapPost("/api/agent/candidates/{id:long}/analyze", IResult (
    CandidateStore store, long id) =>
{
    var job = store.QueueAgentJob(id);
    return job is null ? Results.NotFound() : Results.Accepted($"/api/agent/candidates/{id}", job);
});
app.MapPost("/api/agent/candidates/{id:long}/suggestions/{suggestionId:long}/decision", IResult (
    CandidateStore store, long id, long suggestionId, AgentSuggestionDecision decision) =>
{
    var review = store.DecideAgentSuggestion(id, suggestionId, decision.Accept);
    return review is null ? Results.NotFound() : Results.Ok(review);
});
app.MapPost("/api/agent/candidates/{id:long}/task-plans/{planId:long}/decision", IResult (
    CandidateStore store, GitLabMcpSettingsStore settings, long id, long planId, AgentTaskPlanDecision decision) =>
{
    var review = store.DecideAgentTaskPlan(id, planId, decision.Accept, settings.GetView().DisplayName);
    return review is null ? Results.NotFound() : Results.Ok(review);
});
app.MapPatch("/api/candidates/{id:long}/triage", (CandidateStore store, long id, TriageDecision decision) =>
{
    try { return store.SetTriageStatus(id, decision.Status) is { } candidate ? Results.Ok(candidate) : Results.NotFound(); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.MapPost("/api/candidates/{id:long}/links", (CandidateStore store, long id, CandidateLinkRequest link) =>
{
    try
    {
        if (store.Get(link.CandidateId) is null) return Results.NotFound();
        var candidate = store.AddLink(id, link);
        return candidate is null ? Results.NotFound() : Results.Ok(candidate);
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
});
app.Run();

static string ResolveContentRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TodoGraph.csproj"))) return directory.FullName;
            directory = directory.Parent;
        }
    }
    return AppContext.BaseDirectory;
}

public partial class Program;
