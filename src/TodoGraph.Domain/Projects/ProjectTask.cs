using Common.Domain;
using TodoGraph.Domain.Processes;

namespace TodoGraph.Domain.Projects;

public sealed class ProjectTask : BaseEntity
{
    internal ProjectTask(string description) => Description = Required(description, nameof(description));

    public string? Title { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public ProcessRun? CurrentProcessRun { get; private set; }

    public void StartProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (CurrentProcessRun is not null)
            throw new InvalidOperationException("A ProjectTask can start only one ProcessRun.");

        CurrentProcessRun = ProcessRun.Start(process.CaptureSnapshot());
    }

    public void CompleteCurrentProcessStage(ProcessStageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (CurrentProcessRun is null)
            throw new InvalidOperationException("The ProjectTask has not started a Process.");

        CurrentProcessRun.CompleteCurrentStage(this, result);
    }

    public void SubmitProcessUserResponse(string response)
    {
        if (CurrentProcessRun is null)
            throw new InvalidOperationException("The ProjectTask has not started a Process.");

        CurrentProcessRun.SubmitUserResponse(response);
    }

    public void ApplyProcessUpdate(string? title, string? description)
    {
        if (title is null && description is null)
            throw new ArgumentException("A process update must change a title or description.");

        var normalizedTitle = title is null ? null : Required(title, nameof(title));
        var normalizedDescription = description is null ? null : Required(description, nameof(description));

        if (normalizedTitle is not null) Title = normalizedTitle;
        if (normalizedDescription is not null) Description = normalizedDescription;
    }

    private static string Required(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Project task text is required.", parameterName);
        return normalized;
    }
}
