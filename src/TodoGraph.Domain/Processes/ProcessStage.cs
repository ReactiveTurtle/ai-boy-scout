using Common.Domain;

namespace TodoGraph.Domain.Processes;

public sealed class ProcessStage : BaseEntity
{
    internal ProcessStage(string instructions) => Instructions = Required(instructions);

    public string Instructions { get; private set; }

    internal void UpdateInstructions(string instructions) => Instructions = Required(instructions);

    private static string Required(string? instructions)
    {
        var normalized = instructions?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Stage instructions are required.", nameof(instructions));
        return normalized;
    }
}
