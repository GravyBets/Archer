using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public sealed class ProjectCiVerifier(HttpClient client)
{
    private static string Api(ManagedProject project) => "https://api.github.com/repos/" + project.Repository;
    private static string Workflow(ManagedProject project) => "/actions/workflows/" + project.WorkflowFile;
    public sealed record Verdict(string Status, string Detail);

    public async Task<bool> ReadyAsync(ManagedProject project, CancellationToken ct)
    {
        using var workflow = await ReadAsync(project, Workflow(project), ct);
        return workflow.RootElement.GetProperty("state").GetString() == "active" &&
            workflow.RootElement.GetProperty("path").GetString() == ".github/workflows/" + project.WorkflowFile;
    }

    public async Task<Verdict> InspectAsync(ManagedProject project, CodeProposal proposal, CancellationToken ct, bool requireDraft = false)
    {
        if (!string.Equals(proposal.Repository, project.Repository, StringComparison.OrdinalIgnoreCase) ||
            proposal.PullRequestNumber is not > 0 ||
            !Regex.IsMatch(proposal.PublishedHeadSha ?? "", "^[a-fA-F0-9]{40}$"))
            return new("Blocked", "No immutable published PR identity.");
        using var pr = await ReadAsync(project, "/pulls/" + proposal.PullRequestNumber, ct);
        var p = pr.RootElement;
        var head = p.GetProperty("head");
        if (p.GetProperty("state").GetString() != "open" ||
            (requireDraft && (!p.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.True)) ||
            head.GetProperty("sha").GetString() != proposal.PublishedHeadSha ||
            head.GetProperty("ref").GetString() != proposal.BranchName ||
            !string.Equals(head.GetProperty("repo").GetProperty("full_name").GetString(), proposal.Repository, StringComparison.OrdinalIgnoreCase) ||
            p.GetProperty("base").GetProperty("ref").GetString() != proposal.BaseBranch)
            return new("Blocked", "PR changed, closed, or no longer matches the recorded head.");

        using var runs = await ReadAsync(project, Workflow(project) + "/runs?event=pull_request&head_sha=" +
            proposal.PublishedHeadSha + "&per_page=30", ct);
        var run = runs.RootElement.GetProperty("workflow_runs").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("head_sha").GetString() == proposal.PublishedHeadSha &&
                r.GetProperty("head_branch").GetString() == proposal.BranchName &&
                r.GetProperty("event").GetString() == "pull_request");
        if (run.ValueKind == JsonValueKind.Undefined || run.GetProperty("status").GetString() != "completed")
            return new("Testing", "Waiting for the exact-head project CI run.");
        var runId = run.GetProperty("id").GetInt64();
        using var jobs = await ReadAsync(project, "/actions/runs/" + runId + "/jobs?filter=latest&per_page=30", ct);
        var job = jobs.RootElement.GetProperty("jobs").EnumerateArray()
            .FirstOrDefault(j => j.GetProperty("name").GetString() == project.VerificationJob);
        if (job.ValueKind == JsonValueKind.Undefined)
            return new("Blocked", "Expected independent verification job missing.");
        var steps = job.GetProperty("steps").EnumerateArray().ToArray();
        bool Passed(string name) => steps.Any(s => s.GetProperty("name").GetString() == name &&
            s.GetProperty("conclusion").GetString() == "success");
        var url = "https://github.com/" + project.Repository + "/actions/runs/" + runId;
        if (run.GetProperty("conclusion").GetString() == "success" &&
            job.GetProperty("conclusion").GetString() == "success" &&
            project.RequiredSteps.Count >= 2 && project.RequiredSteps.All(Passed))
            return new("Verified", "Exact-head required build and test steps passed: " + url);
        var failed = steps.Where(s => s.GetProperty("conclusion").GetString() == "failure")
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        if (!failed.Any(s => s is not null && project.RequiredSteps.Contains(s, StringComparer.Ordinal)))
            return new("Blocked", "CI did not complete successfully for an actionable code failure: " + url);
        // Only allow diagnostic codes and source positions from approved
        // files. Do not transmit arbitrary log/annotation text or secrets.
        var feedback = new List<string>();
        if (job.TryGetProperty("check_run_url", out var checkUrl) &&
            Regex.Match(checkUrl.GetString() ?? "",
                "^" + Regex.Escape(Api(project)) + @"/check-runs/([0-9]+)$") is { Success: true } match)
        {
            try
            {
                using var annotations = await ReadAsync(project, "/check-runs/" + match.Groups[1].Value + "/annotations?per_page=30", ct);
                foreach (var annotation in annotations.RootElement.EnumerateArray())
                {
                    var path = annotation.GetProperty("path").GetString() ?? "";
                    if (!proposal.Files.Any(f => f.Path == path)) continue;
                    var text = annotation.GetProperty("message").GetString() ?? "";
                    var code = Regex.Match(text, @"\b(?:CS|RZ)[0-9]{4,5}\b");
                    if (!code.Success) continue;
                    var line = annotation.GetProperty("start_line").GetInt32();
                    feedback.Add(code.Value + " at " + path + ":" + line);
                    if (feedback.Count >= 10) break;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested) { /* step names remain available */ }
        }
        return new("Failed", "Failed CI steps: " + string.Join(", ", failed) +
            (feedback.Count == 0 ? "" : ". Compiler diagnostics: " + string.Join("; ", feedback)) + ". " + url);
    }

    private async Task<JsonDocument> ReadAsync(ManagedProject project, string path, CancellationToken ct)
    {
        if (!ProjectPolicy.ValidRepository(project.Repository)) throw new InvalidOperationException("Invalid repository profile.");
        using var request = new HttpRequestMessage(HttpMethod.Get, Api(project) + path);
        request.Headers.UserAgent.ParseAdd("Archer-Project-CI/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        var token = ProjectGitHubCredentials.Scoped(project.Repository, false);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Project CI lookup failed (HTTP " + (int)response.StatusCode + ").");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
