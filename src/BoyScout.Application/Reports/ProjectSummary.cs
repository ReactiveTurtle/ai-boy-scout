namespace BoyScout.Application.Reports;

public sealed record ProjectSummary(
    long Id,
    string Name,
    string PrimaryPath,
    long ReportCount,
    long OpenCount,
    string? LastReportAt,
    bool AiAccessGranted);
