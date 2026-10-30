using BoyScout.Application.Projects;
using BoyScout.Application.Reports;
using BoyScout.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace BoyScout.Infrastructure.Persistence.Repositories;

public sealed class EfProjectRepository(BoyScoutDbContext context) : IProjectRepository
{
    public async Task<IReadOnlyList<ProjectSummary>> ListProjectSummariesAsync(CancellationToken cancellationToken)
    {
        var rows = await context.Projects.Select(project => new
        {
            project.Id,
            project.Name,
            PrimaryPath = project.Paths.Where(path => path.IsPrimary).Select(path => path.Path).FirstOrDefault(),
            ReportCount = context.Reports.LongCount(report => report.ProjectId == project.Id),
            OpenCount = context.Reports.LongCount(report => report.ProjectId == project.Id && report.Status == "open"),
            LastReportAt = context.Reports.Where(report => report.ProjectId == project.Id)
                .OrderByDescending(report => report.Sequence).Select(report => report.CreatedAt).FirstOrDefault(),
            LastReportSequence = context.Reports.Where(report => report.ProjectId == project.Id)
                .Select(report => (long?)report.Sequence).Max(),
            AiAccessGranted = context.ProjectAiAccess.Any(access => access.ProjectId == project.Id),
        }).Where(project => project.PrimaryPath != null).ToArrayAsync(cancellationToken);

        return rows.OrderByDescending(project => project.LastReportSequence ?? 0)
            .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .Select(project => new ProjectSummary(project.Id, project.Name, project.PrimaryPath!, project.ReportCount,
                project.OpenCount, project.LastReportAt, project.AiAccessGranted)).ToArray();
    }

    public async Task<IReadOnlyList<BoyScoutProject>> ListProjectsWithPathsAsync(CancellationToken cancellationToken) =>
        await context.Projects.Include(project => project.Paths).OrderBy(project => project.Id).ToArrayAsync(cancellationToken);

    public async Task<ProjectSummary?> GetProjectSummaryAsync(long projectId, CancellationToken cancellationToken) =>
        (await ListProjectSummariesAsync(cancellationToken)).SingleOrDefault(project => project.Id == projectId);

    public Task<BoyScoutProject?> FindByPathAsync(string normalizedPath, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(normalizedPath.Trim());
        var query = context.Projects.Include(project => project.Paths).Where(project => project.Paths.Any());
        return OperatingSystem.IsWindows()
            ? query.SingleOrDefaultAsync(project => project.Paths.Any(path => EF.Functions.Collate(path.Path, "NOCASE") == fullPath), cancellationToken)
            : query.SingleOrDefaultAsync(project => project.Paths.Any(path => path.Path == fullPath), cancellationToken);
    }

    public Task<bool> IsPathLinkedAsync(string normalizedPath, CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows()
            ? context.ProjectPaths.AnyAsync(path => EF.Functions.Collate(path.Path, "NOCASE") == normalizedPath, cancellationToken)
            : context.ProjectPaths.AnyAsync(path => path.Path == normalizedPath, cancellationToken);

    public async Task<bool?> GetAiAccessAsync(long projectId, CancellationToken cancellationToken)
    {
        if (!await context.Projects.AnyAsync(project => project.Id == projectId, cancellationToken)) return null;
        return await context.ProjectAiAccess.AnyAsync(access => access.ProjectId == projectId, cancellationToken);
    }

    public async Task<bool> SetAiAccessAsync(long projectId, bool granted, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        if (!await context.Projects.AnyAsync(project => project.Id == projectId, cancellationToken)) return false;
        var existing = await context.ProjectAiAccess.SingleOrDefaultAsync(access => access.ProjectId == projectId, cancellationToken);
        if (granted && existing is null)
            context.ProjectAiAccess.Add(new ProjectAiAccess { ProjectId = projectId, GrantedAt = changedAt.ToString("O") });
        else if (!granted && existing is not null)
            context.ProjectAiAccess.Remove(existing);
        return true;
    }

    public void Add(BoyScoutProject project) => context.Projects.Add(project);
}
