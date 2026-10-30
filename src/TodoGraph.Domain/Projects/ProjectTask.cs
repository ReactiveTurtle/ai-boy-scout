using Common.Domain;

namespace TodoGraph.Domain.Projects;

public sealed class ProjectTask : BaseEntity
{
    internal ProjectTask(string description) => Description = Required(description, nameof(description));

    public string? Title { get; private set; }
    public string Description { get; private set; } = string.Empty;

    public void ApplyProcessUpdate(string? title, string? description)
    {
        if (title is null && description is null)
            throw new ArgumentException("A process update must change a title or description.");

        if (title is not null) Title = Required(title, nameof(title));
        if (description is not null) Description = Required(description, nameof(description));
    }

    private static string Required(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Project task text is required.", parameterName);
        return normalized;
    }
}
