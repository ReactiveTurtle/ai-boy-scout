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

    public static ProcessSnapshot Rehydrate(
        Guid processId,
        string name,
        IReadOnlyList<ProcessBlockSnapshot> blocks,
        IReadOnlyList<ProcessStageSnapshot> stageSnapshots)
    {
        if (blocks.Count == 0 || blocks.Any(block => block.Stages.Count == 0))
            throw new ArgumentException("A Process snapshot must contain blocks with stages.", nameof(blocks));

        var hashes = stageSnapshots.Select(snapshot => snapshot.Hash).ToHashSet(StringComparer.Ordinal);
        if (blocks.SelectMany(block => block.Stages).Any(stage => !hashes.Contains(stage.StageHash)))
            throw new ArgumentException("A Process snapshot stage reference has no matching stage content.", nameof(stageSnapshots));

        return new ProcessSnapshot(processId, name, blocks, stageSnapshots);
    }
}
