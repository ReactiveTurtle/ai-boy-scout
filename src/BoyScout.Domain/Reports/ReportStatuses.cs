namespace BoyScout.Domain.Reports;

public static class ReportStatuses
{
    public const string Open = "open";
    public const string Accepted = "accepted";
    public const string Planned = "planned";
    public const string Resolved = "resolved";
    public const string Dismissed = "dismissed";

    private static readonly HashSet<string> Values =
    [
        Open,
        Accepted,
        Planned,
        Resolved,
        Dismissed,
    ];

    public static bool IsValid(string status) => Values.Contains(status);
}
