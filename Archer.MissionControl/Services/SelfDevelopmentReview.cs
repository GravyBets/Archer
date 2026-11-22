using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public static class SelfDevelopmentReview
{
    public sealed record Finding(string Status, string Detail);
    public sealed record Candidate(Guid BatchId, string Repository, string HeadSha, string BaselineSha,
        int PullRequestNumber, string PullRequestUrl, string BaselineUrl, string CompareUrl,
        int VerifiedTasks, string[] Files, string Status, string NextAction);

    public static IReadOnlyList<Finding> Audit(ArcherState state)
    {
        var project = state.Projects.SingleOrDefault(p => SelfDevelopmentPolicy.IsSelf(p.Repository));
        if (project is null) return [new("Setup required", "Register the protected Archer project. Registration does not grant repository access.")];
        var findings = new List<Finding>();
        if (!SelfDevelopmentPolicy.Configured(project))
            findings.Add(new("Setup required", "Explicit self-project opt-in and at least one approved pilot file are required."));
        if (!ProjectGitHubCredentials.IsConfigured(project.Repository))
            findings.Add(new("Setup required", "Separate scoped repository credentials are required."));
        if (project.Paused) findings.Add(new("Paused", "Owner paused self-development."));
        var batches = state.DevelopmentBatches.Where(b => b.ProjectId == project.Id).ToArray();
        var blocked = batches.SelectMany(b => b.Tasks).Count(t => t.Status == "Blocked");
        if (blocked > 0) findings.Add(new("Attention", $"{blocked} recorded blocked self-development tasks. Inspect their evidence before authorizing repairs."));
        var ready = project.Objectives.Count(o => ProjectPolicy.ObjectiveReady(project, o));
        findings.Add(new("Backlog", $"{ready} open objectives have accepted prerequisites."));
        var candidates = batches.Count(b => TryCandidate(project, b) is not null);
        findings.Add(new("Review", $"{candidates} recorded candidate drafts qualify for a fresh verification check."));
        findings.Add(new("Boundary", "Only explicitly approved pilot files can be proposed. Release, permission, CI, installer and existing test changes remain owner-controlled."));
        return findings;
    }

    public static Candidate? TryCandidate(ManagedProject project, DevelopmentBatch batch)
    {
        if (!SelfDevelopmentPolicy.IsSelf(project.Repository) || !SelfDevelopmentPolicy.Configured(project) ||
            batch.ProjectId != project.Id || batch.Armed || !batch.Cumulative ||
            batch.Tasks.Count == 0 || batch.Tasks.Any(t => t.Status != "Verified" || t.Proposal is null) ||
            !SandboxPolicy.IsHead(batch.WorkspaceOriginalBaseCommitSha) ||
            !SandboxPolicy.IsHead(batch.WorkspaceHeadSha) ||
            batch.WorkspacePullRequestNumber is not > 0 || batch.WorkspaceBaseBranch != "main")
            return null;
        if (batch.ObjectiveId is { } id && project.Objectives.Any(o => o.Id == id &&
            o.Continuous && o.PlanningState is ("Delegated" or "Planning" or "Batch created" or "Reassessment queued")))
            return null;
        var last = batch.Tasks.Last().Proposal!;
        if (batch.Tasks.Any(t => !SelfDevelopmentPolicy.IsSelf(t.Proposal!.Repository) ||
                t.Proposal.PullRequestNumber != batch.WorkspacePullRequestNumber ||
                t.Proposal.BranchName != batch.WorkspaceBranch ||
                !SandboxPolicy.IsHead(t.Proposal.PublishedHeadSha)) ||
            last.PublishedHeadSha != batch.WorkspaceHeadSha || last.BaseBranch != batch.WorkspaceBaseBranch)
            return null;
        var files = batch.Tasks.SelectMany(t => t.Proposal!.Files)
            .Select(f => f.Path).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (files.Length == 0 || batch.Tasks.SelectMany(t => t.Proposal!.Files).Any(f => f.IsNew || f.IsRegressionTest) ||
            files.Any(p => !SelfDevelopmentPolicy.CanEdit(project.Repository, p)))
            return null;
        if (project.RequireSandbox && (batch.Tasks.Last().Sandbox is not { Status: "Passed" } evidence ||
            evidence.HeadSha != batch.WorkspaceHeadSha || evidence.ImageId != SandboxPolicy.ImageFor(project)))
            return null;
        var url = "https://github.com/" + SelfDevelopmentPolicy.Repository;
        return new(batch.Id, SelfDevelopmentPolicy.Repository, batch.WorkspaceHeadSha!,
            batch.WorkspaceOriginalBaseCommitSha!, batch.WorkspacePullRequestNumber.Value,
            url + "/pull/" + batch.WorkspacePullRequestNumber,
            url + "/commit/" + batch.WorkspaceOriginalBaseCommitSha,
            url + "/compare/" + batch.WorkspaceOriginalBaseCommitSha + "..." + batch.WorkspaceHeadSha,
            batch.Tasks.Count, files, "Recorded candidate — live checks required",
            "Review exact diff and CI; keep the currently installed known-good package and state backup. Baseline is a source reference, not proof of the running version. Owner merge, package verification and deployment are separate.");
    }
}
