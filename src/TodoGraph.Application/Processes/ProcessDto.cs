using TodoGraph.Domain.Processes;

namespace TodoGraph.Application.Processes;

public sealed record ProcessDto(Guid Id, string Name, IReadOnlyList<ProcessBlockDto> Blocks)
{
    public static ProcessDto From(Process process) => new(
        process.Id,
        process.Name,
        process.Blocks.Select(ProcessBlockDto.From).ToArray());
}

public sealed record ProcessBlockDto(Guid Id, int Position, IReadOnlyList<ProcessStageDto> Stages)
{
    public static ProcessBlockDto From(ProcessBlock block) => new(
        block.Id,
        block.Position,
        block.Stages.Select(ProcessStageDto.From).ToArray());
}

public sealed record ProcessStageDto(
    Guid Id,
    int Position,
    string Instructions,
    ProcessStageBackwardLink? BackwardLink)
{
    public static ProcessStageDto From(ProcessStage stage) => new(
        stage.Id,
        stage.Position,
        stage.Instructions,
        stage.BackwardLink);
}
