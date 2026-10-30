namespace TodoGraph.Domain.Processes;

public sealed record ProcessStageBackwardLink(Guid TargetStageId, int MaxRepeats);
