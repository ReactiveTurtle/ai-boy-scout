using Common.Domain;

namespace TodoGraph.Domain.Processes;

/// <summary>An ordered group of stages within a Process.</summary>
public sealed class ProcessBlock : BaseEntity
{
    private readonly List<ProcessStage> stages = [];

    internal ProcessBlock(string initialStageInstructions) => stages.Add(new ProcessStage(initialStageInstructions));

    public IReadOnlyList<ProcessStage> Stages => stages.AsReadOnly();

    public ProcessStage AddStage(string instructions)
    {
        var stage = new ProcessStage(instructions);
        stages.Add(stage);
        return stage;
    }

    public void UpdateStageInstructions(Guid stageId, string instructions) =>
        GetStage(stageId).UpdateInstructions(instructions);

    public void RemoveStage(Guid stageId)
    {
        var stage = GetStage(stageId);
        if (stages.Count == 1)
            throw new InvalidOperationException("A ProcessBlock must contain at least one stage.");
        if (stages.Any(candidate => candidate.BackwardLink?.TargetStageId == stageId))
            throw new InvalidOperationException("Remove links targeting this stage before removing it.");

        stages.Remove(stage);
    }

    public void ReorderStages(IReadOnlyList<Guid> orderedStageIds)
    {
        ArgumentNullException.ThrowIfNull(orderedStageIds);

        var currentStageIds = stages.Select(stage => stage.Id).ToHashSet();
        if (orderedStageIds.Count != stages.Count ||
            orderedStageIds.Distinct().Count() != stages.Count ||
            !currentStageIds.SetEquals(orderedStageIds))
        {
            throw new ArgumentException(
                "The new order must contain every stage exactly once.",
                nameof(orderedStageIds));
        }

        var stagesById = stages.ToDictionary(stage => stage.Id);
        var reorderedStages = orderedStageIds.Select(stageId => stagesById[stageId]).ToArray();
        var positions = reorderedStages
            .Select((stage, index) => (stage.Id, index))
            .ToDictionary(item => item.Id, item => item.index);
        foreach (var (stage, sourceIndex) in reorderedStages.Select((stage, index) => (stage, index)))
        {
            if (stage.BackwardLink is { } link && positions[link.TargetStageId] >= sourceIndex)
                throw new ArgumentException("A backward link must target an earlier stage.", nameof(orderedStageIds));
        }

        stages.Clear();
        stages.AddRange(reorderedStages);
    }

    public void ConnectBackward(Guid sourceStageId, Guid targetStageId, int maxRepeats)
    {
        if (maxRepeats <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRepeats), "The repeat limit must be positive.");

        var source = GetStage(sourceStageId);
        var target = GetStage(targetStageId);
        var sourceIndex = stages.IndexOf(source);
        var targetIndex = stages.IndexOf(target);
        if (targetIndex >= sourceIndex)
            throw new ArgumentException("A backward link must target an earlier stage.", nameof(targetStageId));
        if (source.BackwardLink is not null)
            throw new InvalidOperationException("The stage already has a backward link.");

        source.SetBackwardLink(new ProcessStageBackwardLink(targetStageId, maxRepeats));
    }

    public void RemoveBackwardLink(Guid sourceStageId) => GetStage(sourceStageId).SetBackwardLink(null);

    private ProcessStage GetStage(Guid stageId) =>
        stages.SingleOrDefault(stage => stage.Id == stageId)
        ?? throw new ArgumentException("The stage does not belong to this ProcessBlock.", nameof(stageId));
}
