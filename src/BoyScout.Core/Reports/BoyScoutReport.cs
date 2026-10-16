namespace BoyScout.Core.Reports;

public sealed record BoyScoutReport(
    string Id,
    string Project,
    string Title,
    string? Location,
    string Observation,
    string? Impact,
    string? SuggestedAction,
    string? DiscoveredDuring,
    string Status,
    string CreatedAt,
    string UpdatedAt,
    string? SourceName = null,
    Guid? SourceInstallationId = null,
    long? ProjectId = null);
