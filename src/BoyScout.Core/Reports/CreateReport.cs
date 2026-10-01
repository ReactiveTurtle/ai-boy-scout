namespace BoyScout.Core.Reports;

public sealed record CreateReport(
    string Project,
    string Title,
    string? Location,
    string Observation,
    string? Impact,
    string? SuggestedAction,
    string? DiscoveredDuring);
