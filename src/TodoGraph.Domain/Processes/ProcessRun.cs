using System.Collections.ObjectModel;
using Common.Domain;
using TodoGraph.Domain.Projects;

namespace TodoGraph.Domain.Processes;

/// <summary>A single immutable-definition run of a Process on one ProjectTask.</summary>
public sealed class ProcessRun : BaseEntity
{
    private readonly Dictionary<Guid, int> backwardLinkRepeatCounts = [];

    private ProcessRun(ProcessSnapshot snapshot)
        : this(Guid.NewGuid(), snapshot)
    {
    }

    private ProcessRun(Guid id, ProcessSnapshot snapshot)
        : base(id)
    {
        Snapshot = snapshot;
        CurrentBlockId = snapshot.Blocks[0].BlockId;
        CurrentStageId = snapshot.Blocks[0].Stages[0].StageId;
    }

    public ProcessSnapshot Snapshot { get; }
    public ProcessRunStatus Status { get; private set; } = ProcessRunStatus.Running;
    public Guid? CurrentBlockId { get; private set; }
    public Guid? CurrentStageId { get; private set; }
    public string? CurrentUserPrompt { get; private set; }
    public string? PendingUserResponse { get; private set; }
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

    public static ProcessRun Rehydrate(
        Guid id,
        ProcessSnapshot snapshot,
        ProcessRunStatus status,
        Guid? currentBlockId,
        Guid? currentStageId,
        string? currentUserPrompt,
        string? pendingUserResponse,
        string? error,
        IReadOnlyDictionary<Guid, int> repeatCounts)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(repeatCounts);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (status is ProcessRunStatus.Running or ProcessRunStatus.WaitingForUser)
        {
            if (!currentBlockId.HasValue || !currentStageId.HasValue)
                throw new ArgumentException("An active ProcessRun must have a current block and stage.");
        }
        if (status == ProcessRunStatus.WaitingForUser && string.IsNullOrWhiteSpace(currentUserPrompt))
            throw new ArgumentException("A waiting ProcessRun must have a user prompt.", nameof(currentUserPrompt));
        if (status != ProcessRunStatus.WaitingForUser && currentUserPrompt is not null)
            throw new ArgumentException("Only a waiting ProcessRun may have an active user prompt.", nameof(currentUserPrompt));
        if (status == ProcessRunStatus.Blocked && string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("A blocked ProcessRun must have an error.", nameof(error));

        var run = new ProcessRun(id, snapshot)
        {
            Status = status,
            CurrentBlockId = currentBlockId,
            CurrentStageId = currentStageId,
            CurrentUserPrompt = currentUserPrompt,
            PendingUserResponse = pendingUserResponse,
            Error = error,
        };
        foreach (var (stageId, count) in repeatCounts)
        {
            if (count <= 0) throw new ArgumentException("Repeat counts must be positive.", nameof(repeatCounts));
            var stageReference = snapshot.Blocks
                .SelectMany(block => block.Stages)
                .SingleOrDefault(reference => reference.StageId == stageId)
                ?? throw new ArgumentException("A repeat counter references a stage outside the snapshot.", nameof(repeatCounts));
            var stageSnapshot = snapshot.StageSnapshots.Single(item => item.Hash == stageReference.StageHash);
            if (stageSnapshot.BackwardLink is null || count > stageSnapshot.BackwardLink.MaxRepeats)
                throw new ArgumentException("A repeat counter exceeds its backward-link limit.", nameof(repeatCounts));
            run.backwardLinkRepeatCounts.Add(stageId, count);
        }

        return run;
    }

    internal void CompleteCurrentStage(ProjectTask task, ProcessStageResult result)
    {
        if (Status != ProcessRunStatus.Running)
            throw new InvalidOperationException("Only a running ProcessRun can complete a stage.");

        var current = ResolveCurrentStage();
        var prompt = result.UserPrompt is null ? null : RequiredPrompt(result.UserPrompt);
        var hasTaskUpdate = result.Title is not null || result.Description is not null;
        if (!hasTaskUpdate && prompt is null)
            throw new ArgumentException("A completed stage must update the ProjectTask or wait for user input.", nameof(result));

        if (hasTaskUpdate)
            task.ApplyProcessUpdate(result.Title, result.Description);

        CurrentUserPrompt = null;
        PendingUserResponse = null;
        if (prompt is not null)
        {
            CurrentUserPrompt = prompt;
            Status = ProcessRunStatus.WaitingForUser;
            return;
        }

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

    internal void SubmitUserResponse(string response)
    {
        if (Status != ProcessRunStatus.WaitingForUser || CurrentUserPrompt is null)
            throw new InvalidOperationException("The ProcessRun is not waiting for user input.");

        var normalizedResponse = RequiredPrompt(response, nameof(response));
        PendingUserResponse = normalizedResponse;
        CurrentUserPrompt = null;
        Status = ProcessRunStatus.Running;
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

    private static string RequiredPrompt(string? prompt, string parameterName = "prompt")
    {
        var normalized = prompt?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("User prompts and responses must not be empty.", parameterName);
        return normalized;
    }

    private sealed record CurrentStage(
        ProcessBlockSnapshot Block,
        int BlockIndex,
        ProcessStageSnapshotReference StageReference,
        int StageIndex,
        ProcessStageSnapshot Stage);
}
