using BoyScout.Api.Ai;
using BoyScout.Api.Setup;
using BoyScout.Core.Automation;
using BoyScout.Core.Reports;
using BoyScout.Core.Storage;
using BoyScout.Core.Storage.Config;
using BoyScout.Core.Storage.Firebase;
using BoyScout.Core.Storage.Migration;
using BoyScout.Core.Tasks;
using System.Text.Json;

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

var config = new BoyScoutConfigService();
if (config.LoadStatus().Backend != StorageBackend.Sqlite)
{
    throw new InvalidOperationException("Firebase storage is not available until the remote workflow is implemented.");
}

builder.Services.AddSingleton(config);
var database = new BoyScoutDatabase(BoyScoutDatabasePath.Resolve());
database.Migrate();
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<IReportStore>(_ => new ReportStore(database, config));
builder.Services.AddSingleton<ITaskStore>(_ => new TaskStore(database, config));
builder.Services.AddSingleton<IAutomationStore, AutomationStore>();
builder.Services.AddSingleton<RepositoryInstaller>();

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
app.Use(async (context, next) =>
{
    if ((context.Request.Path.StartsWithSegments("/api/setup") ||
         context.Request.Path.StartsWithSegments("/api/storage") ||
         context.Request.Path.StartsWithSegments("/api/remote-projects")) &&
        context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]" or "::1"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    if (context.Request.Path.StartsWithSegments("/api") &&
        (context.Connection.RemoteIpAddress is null ||
         !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    if (context.Request.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        context.Request.Headers.TryGetValue("Origin", out var origin) &&
        (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri) ||
         originUri.Scheme is not ("http" or "https") ||
         !originUri.IsLoopback ||
         (originUri.Port != context.Request.Host.Port && originUri.Port != 4200)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next(context);
});
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/storage", (BoyScoutConfigService service) => Results.Ok(service.LoadStatus()));
app.MapPatch("/api/storage/name", IResult (BoyScoutConfigService service, DisplayNameUpdate request) =>
{
    try { return Results.Ok(service.SetDisplayName(request.Name ?? string.Empty)); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapPost("/api/storage/firebase", IResult (BoyScoutConfigService service, FirebaseConnectionInput request) =>
{
    try
    {
        // Parse the service account key before persisting it. The remote backend is not selected here.
        _ = new FirebaseRealtimeDatabaseTransport(new FirebaseConnection(
            request.DatabaseUrl, request.ProjectId, request.ClientEmail, request.PrivateKey));
        return Results.Ok(service.SaveConnection(request));
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (System.Security.Cryptography.CryptographicException)
    {
        return Results.BadRequest(new { error = "Firebase private key is invalid." });
    }
    catch (FormatException)
    {
        return Results.BadRequest(new { error = "Firebase private key is invalid." });
    }
});
app.MapGet("/api/remote-projects", async Task<IResult> (BoyScoutConfigService service, CancellationToken token) =>
{
    try
    {
        using var transport = new FirebaseRealtimeDatabaseTransport(service.LoadConfiguredFirebaseConnection());
        var repository = new FirebaseReportRepository(transport, new FirebaseSequenceAllocator(transport));
        return Results.Ok(await repository.ListProjectsAsync(token));
    }
    catch (InvalidOperationException exception) when (exception.Message == "Configure Firebase credentials first.")
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    {
        return Results.Problem("Could not read Firebase projects.", statusCode: StatusCodes.Status502BadGateway);
    }
});
app.MapPost("/api/remote-projects", async Task<IResult> (
    BoyScoutConfigService service, CreateRemoteProjectRequest request, CancellationToken token) =>
{
    try
    {
        using var transport = new FirebaseRealtimeDatabaseTransport(service.LoadConfiguredFirebaseConnection());
        var repository = new FirebaseReportRepository(transport, new FirebaseSequenceAllocator(transport));
        var project = await repository.CreateProjectAsync(request.Name ?? string.Empty, token);
        return Results.Created($"/api/remote-projects/{project.Id}", project);
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (InvalidOperationException exception) when (exception.Message == "Configure Firebase credentials first.")
    { return Results.Conflict(new { error = exception.Message }); }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    { return Results.Problem("Could not create Firebase project.", statusCode: StatusCodes.Status502BadGateway); }
});
app.MapGet("/api/remote-projects/bindings", (BoyScoutConfigService service) => Results.Ok(service.ListBindings()));
app.MapPut("/api/remote-projects/{id:long}/binding", async Task<IResult> (
    BoyScoutConfigService service, long id, RemoteProjectBindingRequest request, CancellationToken token) =>
{
    try
    {
        using var transport = new FirebaseRealtimeDatabaseTransport(service.LoadConfiguredFirebaseConnection());
        var repository = new FirebaseReportRepository(transport, new FirebaseSequenceAllocator(transport));
        if (await repository.GetProjectAsync(id, token) is null) return Results.NotFound();
        return Results.Ok(service.Bind(id, request.Path ?? string.Empty));
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (DirectoryNotFoundException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (InvalidOperationException exception) when (exception.Message == "Configure Firebase credentials first.")
    { return Results.Conflict(new { error = exception.Message }); }
    catch (InvalidOperationException exception) when (exception.Message == "Directory is already bound to another project.")
    { return Results.Conflict(new { error = exception.Message }); }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    { return Results.Problem("Could not bind Firebase project.", statusCode: StatusCodes.Status502BadGateway); }
});
app.MapDelete("/api/remote-projects/{id:long}/binding", (BoyScoutConfigService service, long id) =>
{
    try { return service.Unbind(id) ? Results.NoContent() : Results.NotFound(); }
    catch (ArgumentOutOfRangeException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapPatch("/api/remote-projects/{id:long}/ai-consent", IResult (
    BoyScoutConfigService service, long id, RemoteProjectAiConsentRequest request) =>
{
    try { return Results.Ok(service.SetAiReadConsent(id, request.Granted)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (DirectoryNotFoundException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (ArgumentOutOfRangeException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
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
app.MapGet("/api/projects", (IReportStore store) => Results.Ok(store.ListProjects()));
app.MapGet("/api/projects/{id:long}/transfer-preview", IResult (BoyScoutDatabase database, long id) =>
{
    if (id <= 0) return Results.BadRequest(new { error = "Project ID must be positive." });
    try
    {
        var snapshot = new LocalProjectSnapshotReader(database.DatabasePath).Read(id);
        return Results.Ok(new
        {
            projectId = snapshot.Project.Id,
            name = snapshot.Project.Name,
            paths = snapshot.Paths.Select(path => path.Path),
            snapshot.Counts,
            snapshot.Sha256,
            requiresConfirmation = true,
            transferAvailable = false,
        });
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("does not exist", StringComparison.Ordinal))
    { return Results.NotFound(); }
    catch (InvalidOperationException)
    { return Results.Conflict(new { error = "Local project cannot be safely snapshotted." }); }
});
app.MapPost("/api/setup/preview", IResult (RepositoryInstaller installer, IReportStore store, SetupRequest request) =>
{
    try
    {
        var preview = installer.Preview(request.Path);
        if (!store.ListProjects().Any(project => string.Equals(Path.GetFullPath(project.PrimaryPath), preview.Repository, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            preview = preview with { Files = [.. preview.Files, new SetupFile("Local BoyScout database", "Register repository as a site project on confirmation")] };
        return Results.Ok(preview);
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
});
app.MapPost("/api/setup/confirm", IResult (RepositoryInstaller installer, IReportStore store, SetupRequest request) =>
{
    try
    {
        var preview = installer.Install(request.Path, request.Token);
        if (!store.ListProjects().Any(project => string.Equals(Path.GetFullPath(project.PrimaryPath), preview.Repository, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            store.CreateProject(Path.GetFileName(Path.TrimEndingDirectorySeparator(preview.Repository)), preview.Repository);
        return Results.Ok(preview);
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
});
app.MapPost(
    "/api/projects",
    IResult (IReportStore store, CreateProjectRequest request) =>
    {
        try
        {
            return Results.Created("/api/projects", store.CreateProject(request.Name ?? string.Empty, request.Path ?? string.Empty));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    });
app.MapPatch(
    "/api/projects/{id:long}/ai-access",
    IResult (IReportStore store, AiProcessingControl control, long id, ProjectAiAccessUpdate request) =>
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
        IReportStore store,
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
    (IReportStore store, string id) =>
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
        IReportStore store,
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
app.MapPost(
    "/api/notes/{id}/firebase-transfer",
    async Task<IResult> (IReportStore store, BoyScoutConfigService service, string id, CancellationToken token) =>
    {
        BoyScoutReport? note;
        try { note = store.Get(id); }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        if (note is null) return Results.NotFound(new { error = "Local note was not found." });

        ProjectBinding? binding;
        try { binding = service.ResolveByPath(note.Project); }
        catch (ArgumentException exception) { return Results.Conflict(new { error = exception.Message }); }
        if (binding is null)
            return Results.Conflict(new { error = "Bind this local project to a Firebase project before transferring notes." });

        try
        {
            using var transport = new FirebaseRealtimeDatabaseTransport(service.LoadConfiguredFirebaseConnection());
            var repository = new FirebaseReportRepository(transport, new FirebaseSequenceAllocator(transport));
            if (await repository.GetProjectAsync(binding.ProjectId, token) is null)
                return Results.Conflict(new { error = "The bound Firebase project no longer exists." });

            var status = service.LoadStatus();
            var imported = await repository.ImportNoteAsync(binding.ProjectId, note, status.InstallationId,
                status.DisplayName, token);
            return Results.Ok(new
            {
                localNoteId = note.Id,
                firebaseNoteId = imported.Note.Id,
                alreadyImported = imported.AlreadyImported,
                note = imported.Note,
            });
        }
        catch (InvalidOperationException exception) when (exception.Message == "Configure Firebase credentials first.")
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return Results.Problem("Could not transfer the local note to Firebase.", statusCode: StatusCodes.Status502BadGateway);
        }
    });
app.MapGet(
    "/api/notes/{id}/workflow",
    IResult (ITaskStore store, string id) =>
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
    IResult (ITaskStore store, NoteWorkflowsRequest request) =>
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
    IResult (ITaskStore store, string id) =>
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
    async Task<IResult> (ITaskStore store, string id, HttpRequest request, CancellationToken token) =>
    {
        try
        {
            return Results.Ok(store.RestoreRejectedNote(id, await ReadOptionalCommentAsync(request, token) ?? string.Empty));
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
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Invalid JSON comment body." });
        }
    });
app.MapPost(
    "/api/notes/{id}/reprocess",
    async Task<IResult> (ITaskStore store, string id, HttpRequest request, CancellationToken token) =>
    {
        try
        {
            return Results.Ok(store.ReprocessCompletedNote(id, await ReadOptionalCommentAsync(request, token) ?? string.Empty));
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
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Invalid JSON comment body." });
        }
    });
app.MapPost(
    "/api/notes/{id}/rejection-review",
    IResult (ITaskStore store, string id, RejectionReviewRequest request) =>
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
    IResult (ITaskStore store, string id, ClarificationRequest request) =>
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
    (ITaskStore store) =>
    {
        var proposal = store.GetCurrentProposal();
        return proposal is null ? Results.NoContent() : Results.Ok(proposal);
    });
app.MapPost(
    "/api/task-proposals/{id:long}/confirm",
    IResult (ITaskStore store, long id) =>
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
    IResult (ITaskStore store, long id) =>
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
    async Task<IResult> (ITaskStore store, long id, HttpRequest request, CancellationToken token) =>
    {
        try
        {
            return Results.Ok(store.RetryProposal(id, await ReadOptionalCommentAsync(request, token) ?? string.Empty));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Invalid JSON comment body." });
        }
    });
app.MapPost(
    "/api/task-proposals/{id:long}/requeue",
    async Task<IResult> (ITaskStore store, AiProcessingControl control, long id,
        HttpRequest request, CancellationToken token) =>
    {
        try
        {
            var proposal = store.RequeueProcessingProposal(id, await ReadOptionalCommentAsync(request, token) ?? string.Empty);
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
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Invalid JSON comment body." });
        }
    });
app.MapGet(
    "/api/tasks",
    IResult (ITaskStore store, long? projectId, string? status, string? query, long? before, int limit = 40) =>
    {
        try
        {
            return Results.Ok(store.BrowseTasks(projectId, status, query, before, limit));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });
app.MapPatch(
    "/api/tasks/{id}/status",
    IResult (ITaskStore store, string id, TaskStatusUpdate request) =>
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

static async Task<string?> ReadOptionalCommentAsync(HttpRequest request, CancellationToken token)
{
    if (request.ContentLength == 0) return null;
    if (!request.HasJsonContentType())
        throw new BadHttpRequestException("Expected a JSON comment body.", StatusCodes.Status415UnsupportedMediaType);
    return (await request.ReadFromJsonAsync<CommentRequest>(cancellationToken: token))?.Comment;
}

internal sealed record TaskStatusUpdate(string? Status);

internal sealed record AutomationUpdate(bool Enabled);

internal sealed record DisplayNameUpdate(string? Name);

internal sealed record CreateRemoteProjectRequest(string? Name);

internal sealed record RemoteProjectBindingRequest(string? Path);

internal sealed record RemoteProjectAiConsentRequest(bool Granted);

internal sealed record ProjectAiAccessUpdate(bool Granted);

internal sealed record CreateProjectRequest(string? Name, string? Path);

internal sealed record SetupRequest(string? Path, string? Token);

internal sealed record RejectionReviewRequest(string? Reason);

internal sealed record ClarificationRequest(string? Instruction);

internal sealed record CommentRequest(string? Comment);

internal sealed record NoteWorkflowsRequest(IReadOnlyList<string>? Ids);
