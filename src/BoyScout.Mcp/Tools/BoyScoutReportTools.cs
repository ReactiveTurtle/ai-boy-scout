using System.ComponentModel;
using System.Text.Json;
using BoyScout.Core.Reports;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace BoyScout.Mcp.Tools;

[McpServerToolType]
public sealed class BoyScoutReportTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    [McpServerTool(
        Name = "boy_scout_add",
        Title = "Add Boy Scout report",
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Record a verified issue discovered outside the current task scope. This does not authorize fixing it.")]
    public static string Add(
        ReportStore store,
        [Description("Short actionable title")] string title,
        [Description("Verified current behavior or condition")] string observation,
        [Description("Project path; defaults to the server working directory")] string? project = null,
        [Description("File, symbol, or component where the issue was found")] string? location = null,
        [Description("Why the issue matters")] string? impact = null,
        [Description("Possible follow-up, not an instruction to edit now")] string? suggested_action = null,
        [Description("Task, iteration, review, or session reference")] string? discovered_during = null)
    {
        try
        {
            var report = store.Add(
                new CreateReport(
                    project ?? Environment.CurrentDirectory,
                    title,
                    location,
                    observation,
                    impact,
                    suggested_action,
                    discovered_during));
            return Serialize(report);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(
        Name = "boy_scout_list",
        Title = "List Boy Scout reports",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("List recent Boy Scout reports, optionally filtered by project and status.")]
    public static string List(
        ReportStore store,
        [Description("Project path; omit to list reports from all projects")] string? project = null,
        [Description("Optional report status")] ReportStatusArgument? status = null,
        [Description("Maximum number of reports from 1 to 100")] int limit = 50)
    {
        try
        {
            return Serialize(store.List(project, status is null ? null : ToWireValue(status.Value), limit));
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(
        Name = "boy_scout_get",
        Title = "Get Boy Scout report",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Get one Boy Scout report by its stable ID.")]
    public static string Get(
        ReportStore store,
        [Description("Report ID, for example BSR-000001")] string id)
    {
        try
        {
            return Serialize(
                store.Get(id)
                    ?? throw new McpException($"Boy Scout report {id} was not found."));
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(
        Name = "boy_scout_update_status",
        Title = "Update Boy Scout report status",
        OpenWorld = false)]
    [Description("Change workflow status without altering the original observation.")]
    public static string UpdateStatus(
        ReportStore store,
        [Description("Report ID, for example BSR-000001")] string id,
        [Description("New report status")] ReportStatusArgument status)
    {
        try
        {
            return Serialize(
                store.UpdateStatus(id, ToWireValue(status))
                    ?? throw new McpException($"Boy Scout report {id} was not found."));
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string ToWireValue(ReportStatusArgument status) => status switch
    {
        ReportStatusArgument.Open => ReportStatuses.Open,
        ReportStatusArgument.Accepted => ReportStatuses.Accepted,
        ReportStatusArgument.Planned => ReportStatuses.Planned,
        ReportStatusArgument.Resolved => ReportStatuses.Resolved,
        ReportStatusArgument.Dismissed => ReportStatuses.Dismissed,
        _ => throw new McpException($"Unsupported report status: {status}.")
    };
}
