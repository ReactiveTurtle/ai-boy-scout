using Common.Domain;

namespace TodoGraph.Domain.Processes;

public sealed class ProcessStage : BaseEntity
{
    private ProcessStage() => Instructions = string.Empty;

    internal ProcessStage(string instructions) => Instructions = Required(instructions);

    public int Position { get; private set; }
    public string Instructions { get; private set; } = string.Empty;
    public ProcessStageBackwardLink? BackwardLink { get; private set; }

    internal void UpdateInstructions(string instructions) => Instructions = Required(instructions);

    internal void SetBackwardLink(ProcessStageBackwardLink? backwardLink) => BackwardLink = backwardLink;

    internal void SetPosition(int position) => Position = position;

    internal ProcessStageSnapshot CaptureSnapshot() => ProcessStageSnapshot.Create(this);

    private static string Required(string? instructions)
    {
        var normalized = instructions?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Stage instructions are required.", nameof(instructions));
        return normalized;
    }
}
