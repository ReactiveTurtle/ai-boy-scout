using BoyScout.Domain.Reports;

namespace BoyScout.Application.Reports;

public sealed record ReportPage(
    IReadOnlyList<BoyScoutReport> Items,
    long Total,
    long? NextBefore);
