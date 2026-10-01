namespace BoyScout.Core.Tasks;

public sealed record BoyScoutTask(
    string Id,
    long ProjectId,
    string Title,
    string Description,
    string AcceptanceCriteria,
    string Status,
    string CreatedAt,
    string UpdatedAt,
    IReadOnlyList<string> SourceReportIds);
