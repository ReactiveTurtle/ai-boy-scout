namespace BoyScout.Core.Reports;

public sealed record ReportPage(
    IReadOnlyList<BoyScoutReport> Items,
    long Total,
    long? NextBefore);
