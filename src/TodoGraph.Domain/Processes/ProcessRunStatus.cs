namespace TodoGraph.Domain.Processes;

public enum ProcessRunStatus
{
    Running,
    WaitingForUser,
    Completed,
    Blocked,
}
