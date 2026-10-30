using BoyScout.Application.Projects;
using BoyScout.Infrastructure.Persistence;

namespace BoyScout.Infrastructure.Projects;

public sealed class GitRepositoryIdentityResolver : IGitRepositoryIdentityResolver
{
    public string NormalizeExistingDirectory(string path)
    {
        var normalized = ProjectPathNormalizer.Normalize(path);
        if (!Directory.Exists(normalized)) throw new ArgumentException("Project directory does not exist.", nameof(path));
        return normalized;
    }

    public string? Resolve(string normalizedPath) => GitRepositoryIdentity.Resolve(normalizedPath);
}
