using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public sealed class GeoShiftCiVerifier(HttpClient client)
{
    private const string Api = "https://api.github.com/repos/GravyBets/GeoShift";
    private const string Workflow = "/actions/workflows/archer-ci.yml";
    public sealed record Verdict(string Status, string Detail);

    public async Task<bool> ReadyAsync(CancellationToken ct)
    {
        using var workflow = await ReadAsync(Workflow, ct);
        return workflow.RootElement.GetProperty("state").GetString() == "active" &&
            workflow.RootElement.GetProperty("path").GetString() == ".github/workflows/archer-ci.yml";
    }

    public async Task<Verdict> InspectAsync(CodeProposal proposal, CancellationToken ct)
    {
        if (proposal.Repository != "GravyBets/GeoShift" ||
            proposal.PullRequestNumber is not > 0 ||
            !Regex.IsMatch(proposal.PublishedHeadSha ?? "", "^[a-fA-F0-9]{40}$"))
            return new("Blocked", "No immutable published PR identity.");
        using var pr = await ReadAsync("/pulls/" + proposal.PullRequestNumber, ct);
        var p = pr.RootElement;
        var head = p.GetProperty("head");
        if (p.GetProperty("state").GetString() != "open" ||
            head.GetProperty("sha").GetString() != proposal.PublishedHeadSha ||
            head.GetProperty("ref").GetString() != proposal.BranchName ||
            head.GetProperty("repo").GetProperty("full_name").GetString() != proposal.Repository ||
            p.GetProperty("base").GetProperty("ref").GetString() != proposal.BaseBranch)
            return new("Blocked", "PR changed, closed, or no longer matches the recorded head.");

        using var runs = await ReadAsync(Workflow + "/runs?event=pull_request&head_sha=" +
            proposal.PublishedHeadSha + "&per_page=30", ct);
        var run = runs.RootElement.GetProperty("workflow_runs").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("head_sha").GetString() == proposal.PublishedHeadSha &&
                r.GetProperty("head_branch").GetString() == proposal.BranchName &&
                r.GetProperty("event").GetString() == "pull_request");
        if (run.ValueKind == JsonValueKind.Undefined || run.GetProperty("status").GetString() != "completed")
            return new("Testing", "Waiting for the exact-head GeoShift CI run.");
        var runId = run.GetProperty("id").GetInt64();
        using var jobs = await ReadAsync("/actions/runs/" + runId + "/jobs?filter=latest&per_page=30", ct);
        var job = jobs.RootElement.GetProperty("jobs").EnumerateArray()
            .FirstOrDefault(j => j.GetProperty("name").GetString() == "GeoShift build and tests");
        if (job.ValueKind == JsonValueKind.Undefined)
            return new("Blocked", "Expected independent verification job missing.");
        var steps = job.GetProperty("steps").EnumerateArray().ToArray();
        bool Passed(string name) => steps.Any(s => s.GetProperty("name").GetString() == name &&
            s.GetProperty("conclusion").GetString() == "success");
        var url = "https://github.com/GravyBets/GeoShift/actions/runs/" + runId;
        if (run.GetProperty("conclusion").GetString() == "success" &&
            job.GetProperty("conclusion").GetString() == "success" &&
            Passed("Build Web") && Passed("Run core regression tests"))
            return new("Verified", "Exact-head Web build and core tests passed: " + url);
        var failed = steps.Where(s => s.GetProperty("conclusion").GetString() == "failure")
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        if (!failed.Any(s => s is "Build Web" or "Run core regression tests"))
            return new("Blocked", "CI did not complete successfully for an actionable code failure: " + url);
        // Only bounded step names, never raw logs or credentials. A later
        // integration may add sanitized compiler annotations.
        return new("Failed", "Failed CI steps: " + string.Join(", ", failed) + ". " + url);
    }

    private async Task<JsonDocument> ReadAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
        request.Headers.UserAgent.ParseAdd("Archer-GeoShift-CI/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        var token = Environment.GetEnvironmentVariable("ARCHER_GITHUB_READ_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("GeoShift CI lookup failed (HTTP " + (int)response.StatusCode + ").");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
