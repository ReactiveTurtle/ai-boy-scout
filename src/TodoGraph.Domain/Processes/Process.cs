using Common.Domain;

namespace TodoGraph.Domain.Processes;

/// <summary>A global process definition with an explicitly ordered set of stages.</summary>
public sealed class Process : AggregateRoot
{
    private readonly List<ProcessStage> stages = [];

    private Process(string name, string initialStageInstructions)
    {
        Name = Required(name, nameof(name));
        stages.Add(new ProcessStage(initialStageInstructions));
    }

    public string Name { get; private set; }
    public IReadOnlyList<ProcessStage> Stages => stages.AsReadOnly();

    public static Process Create(string name, string initialStageInstructions) => new(name, initialStageInstructions);

    public void Rename(string name) => Name = Required(name, nameof(name));

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
            throw new InvalidOperationException("A Process must contain at least one stage.");

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
                "The new order must contain every Process stage exactly once.",
                nameof(orderedStageIds));
        }

        var stagesById = stages.ToDictionary(stage => stage.Id);
        var reorderedStages = orderedStageIds.Select(stageId => stagesById[stageId]).ToArray();
        stages.Clear();
        stages.AddRange(reorderedStages);
    }

    private ProcessStage GetStage(Guid stageId) =>
        stages.SingleOrDefault(stage => stage.Id == stageId)
        ?? throw new ArgumentException("The stage does not belong to this Process.", nameof(stageId));

    private static string Required(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Process text is required.", parameterName);
        return normalized;
    }
}
