using BoyScout.Application.Reports;
using BoyScout.Common.Persistence;
using BoyScout.Domain.Projects;

namespace BoyScout.Application.Projects;

public sealed class ProjectStore(
    IProjectRepository projects,
    IGitRepositoryIdentityResolver gitIdentity,
    IUnitOfWork unitOfWork) : IProjectStore
{
    public Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken) =>
        projects.ListProjectSummariesAsync(cancellationToken);

    public async Task<ProjectSummary> ResolveOrCreateProjectAsync(string name, string path, CancellationToken cancellationToken)
    {
        var normalizedPath = gitIdentity.NormalizeExistingDirectory(path);
        var existing = await projects.FindByPathAsync(normalizedPath, cancellationToken);
        if (existing is not null)
            return await projects.GetProjectSummaryAsync(existing.Id, cancellationToken)
                ?? throw new InvalidOperationException("The existing project summary could not be read.");
        return await CreateProjectAsync(name, normalizedPath, cancellationToken);
    }

    public async Task<ProjectSummary> CreateProjectAsync(string name, string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Project name is required.", nameof(name));
        var normalizedPath = gitIdentity.NormalizeExistingDirectory(path);
        if (await projects.IsPathLinkedAsync(normalizedPath, cancellationToken))
            throw new InvalidOperationException("This path is already linked to a project.");

        var repositoryIdentity = gitIdentity.Resolve(normalizedPath);
        var existingProject = repositoryIdentity is null
            ? null
            : await FindByRepositoryIdentityAsync(repositoryIdentity, cancellationToken);
        if (existingProject is not null)
        {
            existingProject.AddPath(normalizedPath, isPrimary: false, DateTimeOffset.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return await projects.GetProjectSummaryAsync(existingProject.Id, cancellationToken)
                ?? throw new InvalidOperationException("The project summary was not found after registering the path.");
        }

        var now = DateTimeOffset.UtcNow;
        var project = BoyScoutProject.Create(name, now);
        project.AddPath(normalizedPath, isPrimary: true, now);
        projects.Add(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await projects.GetProjectSummaryAsync(project.Id, cancellationToken)
            ?? throw new InvalidOperationException("The created project summary could not be read back.");
    }

    public Task<bool?> GetAiAccessAsync(long projectId, CancellationToken cancellationToken) =>
        projects.GetAiAccessAsync(projectId, cancellationToken);

    public Task<bool> SetAiAccessAsync(long projectId, bool granted, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var changed = await projects.SetAiAccessAsync(projectId, granted, DateTimeOffset.UtcNow, token);
            if (changed) await unitOfWork.SaveChangesAsync(token);
            return changed;
        }, cancellationToken);

    private async Task<BoyScoutProject?> FindByRepositoryIdentityAsync(string identity, CancellationToken cancellationToken)
    {
        var candidates = await projects.ListProjectsWithPathsAsync(cancellationToken);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var project in candidates)
        foreach (var path in project.Paths)
            if (string.Equals(gitIdentity.Resolve(path.Path), identity, comparison)) return project;
        return null;
    }
}
