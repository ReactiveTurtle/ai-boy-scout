using Common.Domain;

namespace TodoGraph.Domain.Processes;

/// <summary>A global process definition with an explicitly ordered set of blocks.</summary>
public sealed class Process : AggregateRoot
{
    private readonly List<ProcessBlock> blocks = [];

    private Process()
    {
    }

    private Process(string name, string initialStageInstructions)
    {
        Name = Required(name, nameof(name));
        var initialBlock = new ProcessBlock(initialStageInstructions);
        initialBlock.SetPosition(0);
        blocks.Add(initialBlock);
    }

    public string Name { get; private set; } = string.Empty;
    public IReadOnlyList<ProcessBlock> Blocks => blocks.AsReadOnly();

    public static Process Create(string name, string initialStageInstructions) => new(name, initialStageInstructions);

    public void Rename(string name) => Name = Required(name, nameof(name));

    public ProcessBlock AddBlock(string initialStageInstructions)
    {
        var block = new ProcessBlock(initialStageInstructions);
        block.SetPosition(blocks.Count);
        blocks.Add(block);
        return block;
    }

    public void RemoveBlock(Guid blockId)
    {
        var block = GetBlock(blockId);
        if (blocks.Count == 1)
            throw new InvalidOperationException("A Process must contain at least one block.");

        blocks.Remove(block);
        SetBlockPositions();
    }

    public void ReorderBlocks(IReadOnlyList<Guid> orderedBlockIds)
    {
        ArgumentNullException.ThrowIfNull(orderedBlockIds);

        var currentBlockIds = blocks.Select(block => block.Id).ToHashSet();
        if (orderedBlockIds.Count != blocks.Count ||
            orderedBlockIds.Distinct().Count() != blocks.Count ||
            !currentBlockIds.SetEquals(orderedBlockIds))
        {
            throw new ArgumentException(
                "The new order must contain every ProcessBlock exactly once.",
                nameof(orderedBlockIds));
        }

        var blocksById = blocks.ToDictionary(block => block.Id);
        var reorderedBlocks = orderedBlockIds.Select(blockId => blocksById[blockId]).ToArray();
        blocks.Clear();
        blocks.AddRange(reorderedBlocks);
        SetBlockPositions();
    }

    public ProcessSnapshot CaptureSnapshot()
    {
        var stageSnapshotsByHash = new Dictionary<string, ProcessStageSnapshot>(StringComparer.Ordinal);
        var blockSnapshots = blocks
            .Select(block => block.CaptureSnapshot(stageSnapshotsByHash))
            .ToArray();
        var stageSnapshots = stageSnapshotsByHash.Values
            .OrderBy(snapshot => snapshot.Hash, StringComparer.Ordinal)
            .ToArray();

        return new ProcessSnapshot(Id, Name, blockSnapshots, stageSnapshots);
    }

    private ProcessBlock GetBlock(Guid blockId) =>
        blocks.SingleOrDefault(block => block.Id == blockId)
        ?? throw new ArgumentException("The block does not belong to this Process.", nameof(blockId));

    private void SetBlockPositions()
    {
        for (var index = 0; index < blocks.Count; index++)
            blocks[index].SetPosition(index);
    }

    private static string Required(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Process text is required.", parameterName);
        return normalized;
    }
}
