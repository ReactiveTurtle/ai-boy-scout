using Microsoft.EntityFrameworkCore;
using TodoGraph.Application.Processes;
using TodoGraph.Domain.Processes;
using TodoGraph.Infrastructure.Persistence.Entities;

namespace TodoGraph.Infrastructure.Persistence.Repositories;

public sealed class EfProcessRunRepository(TodoGraphDbContext context) : IProcessRunRepository
{
    public async Task<ProcessRun?> GetForProjectTaskAsync(Guid projectTaskId, CancellationToken cancellationToken)
    {
        var run = await context.ProcessRuns.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProjectTaskId == projectTaskId, cancellationToken);
        if (run is null) return null;

        var blockRows = await context.ProcessRunBlocks.AsNoTracking()
            .Where(block => block.ProcessRunId == run.Id)
            .OrderBy(block => block.Position)
            .ToArrayAsync(cancellationToken);
        var stageRows = await context.ProcessRunStages.AsNoTracking()
            .Where(stage => stage.ProcessRunId == run.Id)
            .OrderBy(stage => stage.Position)
            .ToArrayAsync(cancellationToken);
        var stageHashes = stageRows.Select(stage => stage.StageHash).Distinct(StringComparer.Ordinal).ToArray();
        var stageSnapshotRows = await context.ProcessStageSnapshots.AsNoTracking()
            .Where(snapshot => stageHashes.Contains(snapshot.Hash))
            .ToArrayAsync(cancellationToken);
        if (stageSnapshotRows.Length != stageHashes.Length)
            throw new InvalidOperationException("A ProcessRun references a missing stage snapshot.");

        var stageSnapshots = stageSnapshotRows
            .OrderBy(snapshot => snapshot.Hash, StringComparer.Ordinal)
            .Select(snapshot => ProcessStageSnapshot.Rehydrate(
                snapshot.Hash,
                snapshot.Instructions,
                snapshot.BackwardTargetStageId is { } targetStageId && snapshot.BackwardMaxRepeats is { } repeats
                    ? new ProcessStageBackwardLink(targetStageId, repeats)
                    : null))
            .ToArray();
        var blockSnapshots = blockRows.Select(block => ProcessBlockSnapshot.Rehydrate(
            block.BlockId,
            block.Position,
            stageRows
                .Where(stage => stage.BlockId == block.BlockId)
                .OrderBy(stage => stage.Position)
                .Select(stage => new ProcessStageSnapshotReference(stage.StageId, stage.StageHash, stage.Position))
                .ToArray())).ToArray();
        var processSnapshot = ProcessSnapshot.Rehydrate(run.ProcessId, run.ProcessName, blockSnapshots, stageSnapshots);
        var repeatCounts = await context.ProcessRunLinkCounters.AsNoTracking()
            .Where(counter => counter.ProcessRunId == run.Id)
            .ToDictionaryAsync(counter => counter.SourceStageId, counter => counter.RepeatCount, cancellationToken);

        return ProcessRun.Rehydrate(
            run.Id,
            processSnapshot,
            run.Status,
            run.CurrentBlockId,
            run.CurrentStageId,
            run.CurrentUserPrompt,
            run.PendingUserResponse,
            run.Error,
            repeatCounts);
    }

    public async Task SaveAsync(Guid projectTaskId, ProcessRun processRun, CancellationToken cancellationToken)
    {
        var run = await context.ProcessRuns
            .SingleOrDefaultAsync(item => item.ProjectTaskId == projectTaskId, cancellationToken);
        var isNew = run is null;
        if (run is null)
        {
            run = new ProcessRunRecord { Id = processRun.Id, ProjectTaskId = projectTaskId };
            context.ProcessRuns.Add(run);
        }
        else if (run.Id != processRun.Id)
        {
            throw new InvalidOperationException("The ProjectTask already has a different ProcessRun.");
        }

        run.ProcessId = processRun.Snapshot.ProcessId;
        run.ProcessName = processRun.Snapshot.Name;
        run.Status = processRun.Status;
        run.CurrentBlockId = processRun.CurrentBlockId;
        run.CurrentStageId = processRun.CurrentStageId;
        run.CurrentUserPrompt = processRun.CurrentUserPrompt;
        run.PendingUserResponse = processRun.PendingUserResponse;
        run.Error = processRun.Error;

        if (isNew)
        {
            await AddSnapshotAsync(processRun, cancellationToken);
            AddSnapshotReferences(processRun);
        }

        await UpdateRepeatCountsAsync(processRun, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task AddSnapshotAsync(ProcessRun processRun, CancellationToken cancellationToken)
    {
        foreach (var snapshot in processRun.Snapshot.StageSnapshots)
        {
            var existing = await context.ProcessStageSnapshots
                .SingleOrDefaultAsync(item => item.Hash == snapshot.Hash, cancellationToken);
            if (existing is not null)
            {
                if (existing.Instructions != snapshot.Instructions ||
                    existing.BackwardTargetStageId != snapshot.BackwardLink?.TargetStageId ||
                    existing.BackwardMaxRepeats != snapshot.BackwardLink?.MaxRepeats)
                    throw new InvalidOperationException("A stage snapshot hash collision was detected in storage.");
                continue;
            }

            context.ProcessStageSnapshots.Add(new ProcessStageSnapshotRecord
            {
                Hash = snapshot.Hash,
                Instructions = snapshot.Instructions,
                BackwardTargetStageId = snapshot.BackwardLink?.TargetStageId,
                BackwardMaxRepeats = snapshot.BackwardLink?.MaxRepeats,
            });
        }
    }

    private void AddSnapshotReferences(ProcessRun processRun)
    {
        foreach (var block in processRun.Snapshot.Blocks)
        {
            context.ProcessRunBlocks.Add(new ProcessRunBlockRecord
            {
                ProcessRunId = processRun.Id,
                BlockId = block.BlockId,
                Position = block.Position,
            });

            foreach (var stage in block.Stages)
            {
                context.ProcessRunStages.Add(new ProcessRunStageRecord
                {
                    ProcessRunId = processRun.Id,
                    BlockId = block.BlockId,
                    StageId = stage.StageId,
                    StageHash = stage.StageHash,
                    Position = stage.Position,
                });
            }
        }
    }

    private async Task UpdateRepeatCountsAsync(ProcessRun processRun, CancellationToken cancellationToken)
    {
        var existingCounters = await context.ProcessRunLinkCounters
            .Where(counter => counter.ProcessRunId == processRun.Id)
            .ToDictionaryAsync(counter => counter.SourceStageId, cancellationToken);

        foreach (var (sourceStageId, repeatCount) in processRun.BackwardLinkRepeatCounts)
        {
            if (existingCounters.TryGetValue(sourceStageId, out var existing))
            {
                existing.RepeatCount = repeatCount;
                continue;
            }

            var block = processRun.Snapshot.Blocks
                .SingleOrDefault(snapshot => snapshot.Stages.Any(stage => stage.StageId == sourceStageId))
                ?? throw new InvalidOperationException("A repeat counter references a stage outside the ProcessRun snapshot.");
            context.ProcessRunLinkCounters.Add(new ProcessRunLinkCounterRecord
            {
                ProcessRunId = processRun.Id,
                BlockId = block.BlockId,
                SourceStageId = sourceStageId,
                RepeatCount = repeatCount,
            });
        }
    }
}
