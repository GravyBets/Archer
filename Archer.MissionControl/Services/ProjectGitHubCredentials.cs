using System.Security.Cryptography;
using System.Text;

namespace Archer.MissionControl.Services;

public static class ProjectGitHubCredentials
{
    public static string Prefix(string repository) =>
        "ARCHER_REPO_" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(repository.ToLowerInvariant())))[..16];

    public static string? Scoped(string repository, bool write) =>
        Environment.GetEnvironmentVariable(Prefix(repository) + (write ? "_WRITE_TOKEN" : "_READ_TOKEN"));

    public static bool IsConfigured(string repository) =>
        !string.IsNullOrWhiteSpace(Scoped(repository, false)) &&
        !string.IsNullOrWhiteSpace(Scoped(repository, true));

    public static string? ForApiUrl(string url, bool write)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "api.github.com" || uri.Port != 443 ||
            uri.UserInfo.Length != 0) throw new InvalidOperationException("Invalid GitHub API destination.");
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 3 || segments[0] != "repos")
            throw new InvalidOperationException("Expected repository-scoped GitHub API destination.");
        var repository = Uri.UnescapeDataString(segments[1]) + "/" + Uri.UnescapeDataString(segments[2]);
        return Scoped(repository, write) ??
            Environment.GetEnvironmentVariable(write ? "ARCHER_GITHUB_WRITE_TOKEN" : "ARCHER_GITHUB_READ_TOKEN");
    }
}
