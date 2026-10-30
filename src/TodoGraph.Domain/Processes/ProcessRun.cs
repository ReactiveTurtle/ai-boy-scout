using System.Collections.ObjectModel;
using Common.Domain;
using TodoGraph.Domain.Projects;

namespace TodoGraph.Domain.Processes;

/// <summary>A single immutable-definition run of a Process on one ProjectTask.</summary>
public sealed class ProcessRun : BaseEntity
{
    private readonly Dictionary<Guid, int> backwardLinkRepeatCounts = [];

    private ProcessRun(ProcessSnapshot snapshot)
    {
        Snapshot = snapshot;
        CurrentBlockId = snapshot.Blocks[0].BlockId;
        CurrentStageId = snapshot.Blocks[0].Stages[0].StageId;
    }

    public ProcessSnapshot Snapshot { get; }
    public ProcessRunStatus Status { get; private set; } = ProcessRunStatus.Running;
    public Guid? CurrentBlockId { get; private set; }
    public Guid? CurrentStageId { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyDictionary<Guid, int> BackwardLinkRepeatCounts =>
        new ReadOnlyDictionary<Guid, int>(new Dictionary<Guid, int>(backwardLinkRepeatCounts));

    internal static ProcessRun Start(ProcessSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Blocks.Count == 0 || snapshot.Blocks[0].Stages.Count == 0)
            throw new ArgumentException("A ProcessRun requires at least one block and stage.", nameof(snapshot));

        return new ProcessRun(snapshot);
    }

    internal void CompleteCurrentStage(ProjectTask task, ProcessStageResult result)
    {
        if (Status != ProcessRunStatus.Running)
            throw new InvalidOperationException("Only a running ProcessRun can complete a stage.");

        var current = ResolveCurrentStage();
        task.ApplyProcessUpdate(result.Title, result.Description);

        if (current.Stage.BackwardLink is { } backwardLink)
        {
            var repeats = backwardLinkRepeatCounts.GetValueOrDefault(current.StageReference.StageId);
            if (repeats >= backwardLink.MaxRepeats)
            {
                Status = ProcessRunStatus.Blocked;
                Error = $"Stage {current.StageReference.StageId} exceeded its backward-link repeat limit of {backwardLink.MaxRepeats}.";
                return;
            }

            backwardLinkRepeatCounts[current.StageReference.StageId] = repeats + 1;
            CurrentBlockId = current.Block.BlockId;
            CurrentStageId = backwardLink.TargetStageId;
            return;
        }

        AdvanceSequentially(current.BlockIndex, current.StageIndex);
    }

    private void AdvanceSequentially(int blockIndex, int stageIndex)
    {
        var currentBlock = Snapshot.Blocks[blockIndex];
        if (stageIndex + 1 < currentBlock.Stages.Count)
        {
            CurrentStageId = currentBlock.Stages[stageIndex + 1].StageId;
            return;
        }

        if (blockIndex + 1 < Snapshot.Blocks.Count)
        {
            var nextBlock = Snapshot.Blocks[blockIndex + 1];
            CurrentBlockId = nextBlock.BlockId;
            CurrentStageId = nextBlock.Stages[0].StageId;
            return;
        }

        Status = ProcessRunStatus.Completed;
        CurrentBlockId = null;
        CurrentStageId = null;
    }

    private CurrentStage ResolveCurrentStage()
    {
        if (CurrentBlockId is not { } blockId || CurrentStageId is not { } stageId)
            throw new InvalidOperationException("The running ProcessRun has no current stage.");

        for (var blockIndex = 0; blockIndex < Snapshot.Blocks.Count; blockIndex++)
        {
            var block = Snapshot.Blocks[blockIndex];
            if (block.BlockId != blockId) continue;

            for (var stageIndex = 0; stageIndex < block.Stages.Count; stageIndex++)
            {
                var stageReference = block.Stages[stageIndex];
                if (stageReference.StageId != stageId) continue;

                var stage = Snapshot.StageSnapshots.SingleOrDefault(snapshot => snapshot.Hash == stageReference.StageHash)
                    ?? throw new InvalidOperationException("The current stage snapshot is missing.");
                return new CurrentStage(block, blockIndex, stageReference, stageIndex, stage);
            }
        }

        throw new InvalidOperationException("The current stage is not present in the ProcessRun snapshot.");
    }

    private sealed record CurrentStage(
        ProcessBlockSnapshot Block,
        int BlockIndex,
        ProcessStageSnapshotReference StageReference,
        int StageIndex,
        ProcessStageSnapshot Stage);
}
