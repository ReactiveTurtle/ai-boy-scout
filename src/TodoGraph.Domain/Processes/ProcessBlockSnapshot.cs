namespace TodoGraph.Domain.Processes;

public sealed class ProcessBlockSnapshot
{
    internal ProcessBlockSnapshot(Guid blockId, int position, IEnumerable<ProcessStageSnapshotReference> stages)
    {
        BlockId = blockId;
        Position = position;
        Stages = Array.AsReadOnly(stages.ToArray());
    }

    public Guid BlockId { get; }
    public int Position { get; }
    public IReadOnlyList<ProcessStageSnapshotReference> Stages { get; }

    public static ProcessBlockSnapshot Rehydrate(Guid blockId, int position, IReadOnlyList<ProcessStageSnapshotReference> stages) =>
        new(blockId, position, stages);
}
