namespace TodoGraph.Domain.Processes;

public sealed record ProcessStageResult(string? Title, string? Description, string? UserPrompt = null);
