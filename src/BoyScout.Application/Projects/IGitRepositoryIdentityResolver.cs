namespace BoyScout.Application.Projects;

public interface IGitRepositoryIdentityResolver
{
    string NormalizeExistingDirectory(string path);
    string? Resolve(string normalizedPath);
}
