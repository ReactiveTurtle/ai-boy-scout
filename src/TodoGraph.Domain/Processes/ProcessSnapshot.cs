namespace TodoGraph.Domain.Processes;

public sealed class ProcessSnapshot
{
    internal ProcessSnapshot(
        Guid processId,
        string name,
        IEnumerable<ProcessBlockSnapshot> blocks,
        IEnumerable<ProcessStageSnapshot> stageSnapshots)
    {
        ProcessId = processId;
        Name = name;
        Blocks = Array.AsReadOnly(blocks.ToArray());
        StageSnapshots = Array.AsReadOnly(stageSnapshots.ToArray());
    }

    public Guid ProcessId { get; }
    public string Name { get; }
    public IReadOnlyList<ProcessBlockSnapshot> Blocks { get; }
    public IReadOnlyList<ProcessStageSnapshot> StageSnapshots { get; }
}
