using TodoGraph.Domain.Processes;

namespace TodoGraph.Infrastructure.Persistence.Entities;

public sealed class ProcessRunRecord
{
    public Guid Id { get; set; }
    public Guid ProjectTaskId { get; set; }
    public Guid ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public ProcessRunStatus Status { get; set; }
    public Guid? CurrentBlockId { get; set; }
    public Guid? CurrentStageId { get; set; }
    public string? CurrentUserPrompt { get; set; }
    public string? PendingUserResponse { get; set; }
    public string? Error { get; set; }
}

public sealed class ProcessRunBlockRecord
{
    public Guid ProcessRunId { get; set; }
    public Guid BlockId { get; set; }
    public int Position { get; set; }
}

public sealed class ProcessRunStageRecord
{
    public Guid ProcessRunId { get; set; }
    public Guid BlockId { get; set; }
    public Guid StageId { get; set; }
    public string StageHash { get; set; } = string.Empty;
    public int Position { get; set; }
}

public sealed class ProcessStageSnapshotRecord
{
    public string Hash { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public Guid? BackwardTargetStageId { get; set; }
    public int? BackwardMaxRepeats { get; set; }
}

public sealed class ProcessRunLinkCounterRecord
{
    public Guid ProcessRunId { get; set; }
    public Guid BlockId { get; set; }
    public Guid SourceStageId { get; set; }
    public int RepeatCount { get; set; }
}
