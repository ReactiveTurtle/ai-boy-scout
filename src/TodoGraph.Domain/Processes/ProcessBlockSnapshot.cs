namespace TodoGraph.Domain.Processes;

public sealed class ProcessBlockSnapshot
{
    internal ProcessBlockSnapshot(Guid blockId, IEnumerable<ProcessStageSnapshotReference> stages)
    {
        BlockId = blockId;
        Stages = Array.AsReadOnly(stages.ToArray());
    }

    public Guid BlockId { get; }
    public IReadOnlyList<ProcessStageSnapshotReference> Stages { get; }
}
