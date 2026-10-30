using TodoGraph.Domain.Processes;

namespace TodoGraph.Application.Processes;

public sealed class ProcessCatalogHandler(IProcessRepository processes)
{
    public async Task<IReadOnlyList<ProcessDto>> ListAsync(CancellationToken cancellationToken) =>
        (await processes.ListAsync(cancellationToken))
        .Select(ProcessDto.From)
        .ToArray();

    public async Task<ProcessDto?> GetAsync(Guid processId, CancellationToken cancellationToken)
    {
        var process = await processes.GetByIdAsync(processId, cancellationToken);
        return process is null ? null : ProcessDto.From(process);
    }

    public async Task<ProcessDto> CreateAsync(string name, string initialStageInstructions, CancellationToken cancellationToken)
    {
        var process = Process.Create(name, initialStageInstructions);
        await processes.AddAsync(process, cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> RenameAsync(Guid processId, string name, CancellationToken cancellationToken)
    {
        var process = await processes.GetByIdAsync(processId, cancellationToken);
        if (process is null) return null;

        process.Rename(name);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> AddBlockAsync(Guid processId, string initialStageInstructions, CancellationToken cancellationToken)
    {
        var process = await processes.GetByIdAsync(processId, cancellationToken);
        if (process is null) return null;

        process.AddBlock(initialStageInstructions);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> RemoveBlockAsync(Guid processId, Guid blockId, CancellationToken cancellationToken)
    {
        var process = await processes.GetByIdAsync(processId, cancellationToken);
        if (process is null || process.Blocks.All(block => block.Id != blockId)) return null;

        process.RemoveBlock(blockId);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> ReorderBlocksAsync(Guid processId, IReadOnlyList<Guid> blockIds, CancellationToken cancellationToken)
    {
        var process = await processes.GetByIdAsync(processId, cancellationToken);
        if (process is null) return null;

        process.ReorderBlocks(blockIds);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> AddStageAsync(Guid processId, Guid blockId, string instructions, CancellationToken cancellationToken)
    {
        var (process, block) = await GetProcessBlockAsync(processId, blockId, cancellationToken);
        if (process is null || block is null) return null;

        block.AddStage(instructions);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> UpdateStageInstructionsAsync(
        Guid processId,
        Guid blockId,
        Guid stageId,
        string instructions,
        CancellationToken cancellationToken)
    {
        var (process, block) = await GetProcessBlockAsync(processId, blockId, cancellationToken);
        if (process is null || block is null || block.Stages.All(stage => stage.Id != stageId)) return null;

        block.UpdateStageInstructions(stageId, instructions);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> RemoveStageAsync(Guid processId, Guid blockId, Guid stageId, CancellationToken cancellationToken)
    {
        var (process, block) = await GetProcessBlockAsync(processId, blockId, cancellationToken);
        if (process is null || block is null || block.Stages.All(stage => stage.Id != stageId)) return null;

        block.RemoveStage(stageId);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> ReorderStagesAsync(
        Guid processId,
        Guid blockId,
        IReadOnlyList<Guid> stageIds,
        CancellationToken cancellationToken)
    {
        var (process, block) = await GetProcessBlockAsync(processId, blockId, cancellationToken);
        if (process is null || block is null) return null;

        block.ReorderStages(stageIds);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> SetBackwardLinkAsync(
        Guid processId,
        Guid blockId,
        Guid sourceStageId,
        Guid targetStageId,
        int maxRepeats,
        CancellationToken cancellationToken)
    {
        var (process, block) = await GetProcessBlockAsync(processId, blockId, cancellationToken);
        if (process is null || block is null || block.Stages.All(stage => stage.Id != sourceStageId) ||
            block.Stages.All(stage => stage.Id != targetStageId))
            return null;

        block.SetBackwardLink(sourceStageId, targetStageId, maxRepeats);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    public async Task<ProcessDto?> RemoveBackwardLinkAsync(
        Guid processId,
        Guid blockId,
        Guid sourceStageId,
        CancellationToken cancellationToken)
    {
        var (process, block) = await GetProcessBlockAsync(processId, blockId, cancellationToken);
        if (process is null || block is null || block.Stages.All(stage => stage.Id != sourceStageId)) return null;

        block.RemoveBackwardLink(sourceStageId);
        await processes.SaveChangesAsync(cancellationToken);
        return ProcessDto.From(process);
    }

    private async Task<(Process? Process, ProcessBlock? Block)> GetProcessBlockAsync(
        Guid processId,
        Guid blockId,
        CancellationToken cancellationToken)
    {
        var process = await processes.GetByIdAsync(processId, cancellationToken);
        return (process, process?.Blocks.SingleOrDefault(block => block.Id == blockId));
    }
}
