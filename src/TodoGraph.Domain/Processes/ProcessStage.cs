using Common.Domain;

namespace TodoGraph.Domain.Processes;

public sealed class ProcessStage : BaseEntity
{
    internal ProcessStage(string instructions) => Instructions = Required(instructions);

    public string Instructions { get; private set; }
    public ProcessStageBackwardLink? BackwardLink { get; private set; }

    internal void UpdateInstructions(string instructions) => Instructions = Required(instructions);

    internal void SetBackwardLink(ProcessStageBackwardLink? backwardLink) => BackwardLink = backwardLink;

    internal ProcessStageSnapshot CaptureSnapshot() => ProcessStageSnapshot.Create(this);

    private static string Required(string? instructions)
    {
        var normalized = instructions?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Stage instructions are required.", nameof(instructions));
        return normalized;
    }
}
