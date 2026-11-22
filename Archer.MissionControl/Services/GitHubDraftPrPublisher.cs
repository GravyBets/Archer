using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Intentionally narrow GitHub writer: only creates an isolated branch and
/// DRAFT PR after a direct owner approval. Cannot merge, delete or deploy.
/// Requires a separately configured, repo-scoped fine-grained write token.
/// </summary>
public sealed class GitHubDraftPrPublisher(HttpClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("ARCHER_CODING_ENABLED"), "true",
            StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARCHER_GITHUB_WRITE_TOKEN"));

    private static (string Owner, string Repo) CheckRepository(CodeProposal proposal)
    {
        if (proposal.Repository.Length > 140 ||
            !Regex.IsMatch(proposal.Repository, @"^[A-Za-z0-9-]{1,39}/[A-Za-z0-9_.-]{1,100}$"))
            throw new InvalidOperationException("Proposal repository name is invalid.");

        var configured = Environment.GetEnvironmentVariable("ARCHER_GITHUB_WRITE_REPOS") ?? "";
        var list = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!list.Contains(proposal.Repository, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "This repository is not approved for publishing. Set ARCHER_GITHUB_WRITE_REPOS to its exact owner/repository name.");

        var parts = proposal.Repository.Split('/');
        if (!Regex.IsMatch(proposal.BaseBranch, @"^[A-Za-z0-9_.-]{1,80}$"))
            throw new InvalidOperationException("Base branch is not supported for publishing.");

        if (proposal.Files.Count is < 1 or > 2 ||
            proposal.Files.Any(x => x.ProposedContent.Length is < 1 or > 12000 ||
                x.ExpectedBlobSha.Length != 40 ||
                !Regex.IsMatch(x.Path, @"^[A-Za-z0-9_.\-/]{1,180}$") ||
                !(x.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    x.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) ||
                x.Path.Split('/').Any(part => part is "." or "..") ||
                x.Path.StartsWith(".github/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Proposal includes an unsupported file or oversized code change.");
        return (parts[0], parts[1]);
    }

    public async Task<(string Url, string Branch)> PublishAsync(CodeProposal proposal, CancellationToken ct)
    {
        if (!IsEnabled)
            throw new InvalidOperationException(
                "GitHub code publishing is disabled. Explicitly configure ARCHER_CODING_ENABLED=true and a repo-scoped ARCHER_GITHUB_WRITE_TOKEN before trying again.");

        var (owner, repo) = CheckRepository(proposal);
        var baseUri = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";
        var branch = "archer/draft-" + proposal.Id.ToString("N")[..12];

        // Capture the exact base commit FIRST, and validate all reviewed blobs
        // against that immutable snapshot. A moving default branch cannot
        // invalidate the checks between verification and branch creation.
        using var head = await RequestAsync(HttpMethod.Get,
            baseUri + "/git/ref/heads/" + Uri.EscapeDataString(proposal.BaseBranch),
            null, ct);
        var commit = head.RootElement.GetProperty("object").GetProperty("sha").GetString() ??
            throw new InvalidOperationException("GitHub did not return the base branch commit.");
        if (!Regex.IsMatch(commit, @"^[0-9a-fA-F]{40}$"))
            throw new InvalidOperationException("GitHub returned an invalid base commit.");

        foreach (var file in proposal.Files)
        {
            var path = string.Join("/", file.Path.Split('/').Select(Uri.EscapeDataString));
            using var existing = await RequestAsync(HttpMethod.Get,
                baseUri + "/contents/" + path + "?ref=" + Uri.EscapeDataString(commit),
                null, ct);
            if (!existing.RootElement.TryGetProperty("sha", out var current) ||
                !string.Equals(current.GetString(), file.ExpectedBlobSha, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"File {file.Path} has changed on GitHub since the proposal was generated. Create a fresh proposal; no changes were published.");
        }

        await RequestAsyncDiscard(HttpMethod.Post, baseUri + "/git/refs",
            new { @ref = "refs/heads/" + branch, sha = commit }, ct);

        // Serial writes are required by GitHub's Contents API.
        foreach (var file in proposal.Files)
        {
            var path = string.Join("/", file.Path.Split('/').Select(Uri.EscapeDataString));
            await RequestAsyncDiscard(HttpMethod.Put, baseUri + "/contents/" + path, new
            {
                message = "[Archer draft] " + proposal.Title,
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(file.ProposedContent)),
                sha = file.ExpectedBlobSha,
                branch
            }, ct);
        }

        var body = new StringBuilder();
        body.AppendLine("## Archer-generated draft — CEO review required");
        body.AppendLine();
        body.AppendLine(proposal.Summary);
        body.AppendLine();
        body.AppendLine("### Owner's request");
        body.AppendLine(proposal.Request);
        body.AppendLine();
        body.AppendLine("### Recommended verification (NOT executed by Archer)");
        body.AppendLine(proposal.TestRecommendations);
        body.AppendLine();
        body.AppendLine("Changes were committed on an isolated branch. Archer did not run a build or tests. " +
                        "Review the diff, inspect workflow checks if configured, and approve any merge or deployment separately.");

        using var pull = await RequestAsync(HttpMethod.Post, baseUri + "/pulls",
            new
            {
                title = "[Archer draft] " + proposal.Title,
                head = branch,
                @base = proposal.BaseBranch,
                body = body.ToString(),
                draft = true,
                maintainer_can_modify = false
            }, ct);
        var url = pull.RootElement.GetProperty("html_url").GetString() ??
            throw new InvalidOperationException("GitHub did not provide a pull request URL.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Host != "github.com" || uri.Scheme != "https")
            throw new InvalidOperationException("GitHub returned an unexpected pull request URL.");

        return (url, branch);
    }

    private async Task RequestAsyncDiscard(HttpMethod method, string url, object body, CancellationToken ct)
    {
        using var _ = await RequestAsync(method, url, body, ct);
    }

    private async Task<JsonDocument> RequestAsync(
        HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Archer", "0.9.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Environment.GetEnvironmentVariable("ARCHER_GITHUB_WRITE_TOKEN"));
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GitHub rejected the draft PR operation (HTTP {(int)response.StatusCode}). " +
                "Check write permissions (Contents and Pull requests), branch protection, and the GitHub repo. " +
                "If a branch was created, inspect it manually; Archer will not retry automatically.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
