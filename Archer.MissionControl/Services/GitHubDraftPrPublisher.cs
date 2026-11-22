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

    public bool CanPublishRepository(string repo) =>
        string.Equals(Environment.GetEnvironmentVariable("ARCHER_CODING_ENABLED"), "true", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(ProjectGitHubCredentials.Scoped(repo, true) ?? Environment.GetEnvironmentVariable("ARCHER_GITHUB_WRITE_TOKEN"));

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
                !(new[] { ".cs", ".razor", ".js", ".jsx", ".ts", ".tsx", ".py" }.Contains(Path.GetExtension(x.Path), StringComparer.OrdinalIgnoreCase)) ||
                x.Path.Split('/').Any(part => part is "." or "..") ||
                x.Path.StartsWith(".github/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Proposal includes an unsupported file or oversized code change.");
        return (parts[0], parts[1]);
    }

    public async Task<(string Url, string Branch)> PublishAsync(CodeProposal proposal, CancellationToken ct)
    {
        if (!CanPublishRepository(proposal.Repository))
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

        proposal.PublishedHeadSha = pull.RootElement.GetProperty("head").GetProperty("sha").GetString();
        proposal.PullRequestNumber = pull.RootElement.GetProperty("number").GetInt32();
        return (url, branch);
    }

    // Create a complete Git tree and commit before exposing any branch.
    // Never write the default branch, force-push, merge or execute generated code locally.
    public async Task<(string Url, string Branch)> PublishWorkspaceAsync(
        ManagedProject project, CodeProposal proposal, CancellationToken ct)
    {
        if (!proposal.WorkspaceDraft || !string.Equals(project.Repository, proposal.Repository, StringComparison.OrdinalIgnoreCase) ||
            !ProjectPolicy.ValidRepository(project.Repository) || project.Paused ||
            !CanPublishRepository(project.Repository) || !ProjectGitHubCredentials.IsConfigured(project.Repository) ||
            !(Environment.GetEnvironmentVariable("ARCHER_GITHUB_WRITE_REPOS") ?? "").Split(',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(project.Repository, StringComparer.OrdinalIgnoreCase) ||
            !Regex.IsMatch(proposal.BaseBranch, @"^[A-Za-z0-9_.-]{1,80}$") ||
            !Regex.IsMatch(proposal.BaseTreeSha ?? "", @"^[0-9a-fA-F]{40}$") ||
            proposal.Files.Count is < 1 or > ProjectPolicy.MaxWorkspaceFiles ||
            proposal.Files.Count(f => f.IsNew) > 2 ||
            proposal.Files.Sum(f => (long)f.ProposedContent.Length) > ProjectPolicy.MaxGeneratedCharacters ||
            proposal.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != proposal.Files.Count ||
            proposal.Files.Any(f => f.ProposedContent.Length is < 1 or > 12000 ||
                !ProjectPolicy.IsApplicationPath(f.Path) || !ProjectPolicy.InSourceRoot(project, f.Path) ||
                (f.IsNew ? !ProjectPolicy.NewSourceAllowed(project, f.Path) || f.ExpectedBlobSha.Length != 0 :
                    !Regex.IsMatch(f.ExpectedBlobSha, @"^[0-9a-fA-F]{40}$"))))
            throw new InvalidOperationException("Workspace publication refused: authorization, path or size validation failed.");

        var api = "https://api.github.com/repos/" + project.Repository;
        var branch = "archer/draft-" + proposal.Id.ToString("N")[..12];
        using var head = await RequestAsync(HttpMethod.Get, api + "/git/ref/heads/" + Uri.EscapeDataString(proposal.BaseBranch), null, ct);
        var baseCommit = RequireSha(head.RootElement.GetProperty("object").GetProperty("sha"));
        using var commit = await RequestAsync(HttpMethod.Get, api + "/git/commits/" + baseCommit, null, ct);
        var baseTree = RequireSha(commit.RootElement.GetProperty("tree").GetProperty("sha"));
        if (!string.Equals(baseTree, proposal.BaseTreeSha, StringComparison.Ordinal))
            throw new InvalidOperationException("Repository changed since workspace inspection; no branch created.");
        using var tree = await RequestAsync(HttpMethod.Get, api + "/git/trees/" + baseTree + "?recursive=1", null, ct);
        if (tree.RootElement.GetProperty("truncated").GetBoolean())
            throw new InvalidOperationException("Cannot validate a truncated repository tree.");
        var entries = tree.RootElement.GetProperty("tree").EnumerateArray().ToArray();
        if (entries.Length > 12_000) throw new InvalidOperationException("Repository exceeds bounded workspace limit.");
        var byPath = entries.ToDictionary(x => x.GetProperty("path").GetString()!, StringComparer.Ordinal);
        foreach (var file in proposal.Files)
        {
            if (file.IsNew)
            {
                if (entries.Any(x =>
                {
                    var p = x.GetProperty("path").GetString()!;
                    return p.Equals(file.Path, StringComparison.OrdinalIgnoreCase) ||
                        p.StartsWith(file.Path + "/", StringComparison.OrdinalIgnoreCase) ||
                        (file.Path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase) &&
                            (x.GetProperty("type").GetString() != "tree" || !file.Path.StartsWith(p + "/", StringComparison.Ordinal)));
                }))
                    throw new InvalidOperationException("New source path conflicts with an existing entry; no branch created.");
            }
            else if (!byPath.TryGetValue(file.Path, out var existing) ||
                existing.GetProperty("mode").GetString() != "100644" ||
                existing.GetProperty("type").GetString() != "blob" ||
                existing.GetProperty("sha").GetString() != file.ExpectedBlobSha)
                throw new InvalidOperationException("Original file no longer matches its approved regular blob.");
        }
        if (proposal.Files.Any(f => proposal.Files.Any(other =>
            other.Path.StartsWith(f.Path + "/", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Workspace file paths collide.");

        // GitHub's tree API accepts UTF-8 file content. One tree and one commit
        // represent the whole change-set; no partially edited branch is visible.
        using var nextTree = await RequestAsync(HttpMethod.Post, api + "/git/trees", new
        {
            base_tree = baseTree,
            tree = proposal.Files.Select(f => new { path = f.Path, mode = "100644", type = "blob", content = f.ProposedContent }).ToArray()
        }, ct);
        var treeSha = RequireSha(nextTree.RootElement.GetProperty("sha"));
        using var nextCommit = await RequestAsync(HttpMethod.Post, api + "/git/commits", new
        {
            message = "[Archer workspace] " + proposal.Title,
            tree = treeSha, parents = new[] { baseCommit }
        }, ct);
        var commitSha = RequireSha(nextCommit.RootElement.GetProperty("sha"));
        await RequestAsyncDiscard(HttpMethod.Post, api + "/git/refs",
            new { @ref = "refs/heads/" + branch, sha = commitSha }, ct);
        using var pull = await RequestAsync(HttpMethod.Post, api + "/pulls", new
        {
            title = "[Archer draft] " + proposal.Title, head = branch, @base = proposal.BaseBranch,
            body = "## Owner-authorized isolated workspace\n" + proposal.Summary +
                "\n\nOwner objective: " + proposal.Request + "\n\nVerification recommendations (not executed yet): " +
                proposal.TestRecommendations + "\n\nReview required. Archer never merges or deploys.",
            draft = true, maintainer_can_modify = false
        }, ct);
        var number = pull.RootElement.GetProperty("number").GetInt32();
        var actualHead = RequireSha(pull.RootElement.GetProperty("head").GetProperty("sha"));
        var url = pull.RootElement.GetProperty("html_url").GetString();
        if (number <= 0 || actualHead != commitSha || !string.Equals(url, "https://github.com/" + proposal.Repository + "/pull/" + number, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected PR identity: inspect the isolated branch manually.");
        proposal.PublishedHeadSha = actualHead;
        proposal.PullRequestNumber = number;
        return (url!, branch);
    }

    private static string RequireSha(JsonElement value)
    {
        var sha = value.GetString() ?? "";
        return Regex.IsMatch(sha, @"^[0-9a-fA-F]{40}$") ? sha :
            throw new InvalidOperationException("GitHub returned an invalid object identity.");
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
            "Bearer", ProjectGitHubCredentials.ForApiUrl(url, true));
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
