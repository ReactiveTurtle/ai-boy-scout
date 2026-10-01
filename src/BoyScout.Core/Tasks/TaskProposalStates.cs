namespace BoyScout.Core.Tasks;

public static class TaskProposalStates
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Unverified = "unverified";
    public const string Failed = "failed";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
}
