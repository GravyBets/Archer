using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public static class ContinuousObjectivePolicy
{
    public const int MaximumCycles = 4;
    public const int MaximumCodingAttempts = 12;

    public static bool Authorized(ProjectObjective objective, DateTimeOffset now) =>
        objective.Continuous && objective.AcceptedAtUtc is null &&
        objective.DelegatedUntilUtc > now &&
        objective.MaxPlanningCycles is >= 2 and <= MaximumCycles &&
        objective.MaxCodingAttempts is >= 1 and <= MaximumCodingAttempts;

    public static bool CanContinue(ManagedProject project, ProjectObjective objective, DevelopmentBatch batch, DateTimeOffset now) =>
        Authorized(objective, now) && !project.Paused && project.CumulativeWorkspaces &&
        ProjectPolicy.ObjectiveReady(project, objective) &&
        batch.Continuous && batch.Cumulative && batch.ProjectId == project.Id && batch.ObjectiveId == objective.Id &&
        !batch.Armed && batch.Tasks.Count > 0 && batch.Tasks.All(t => t.Status == "Verified") &&
        batch.WorkspaceBranch is not null && SandboxPolicy.IsHead(batch.WorkspaceHeadSha) &&
        batch.WorkspacePullRequestNumber is > 0 && batch.WorkspaceBaseBranch is not null &&
        batch.Tasks.Last().Proposal?.PublishedHeadSha == batch.WorkspaceHeadSha &&
        batch.PlanningCycles == objective.PlanningCyclesUsed &&
        batch.ExpiresAtUtc == objective.DelegatedUntilUtc;

    public static int RemainingAttempts(ProjectObjective objective, DevelopmentBatch batch) =>
        Math.Max(0, objective.MaxCodingAttempts - batch.Tasks.Sum(t => t.Attempts));

    public static void Stop(ProjectObjective objective, string state, string detail)
    {
        objective.PlanningState = state;
        objective.PlanningDetail = detail;
        objective.DelegatedUntilUtc = null;
    }

    // Only a fully verified cycle may queue the next paid reassessment.
    public static void Reconcile(ManagedProject project, ProjectObjective objective, DevelopmentBatch batch, DateTimeOffset now)
    {
        if (!objective.Continuous || objective.PlanningState != "Batch created" || batch.Armed) return;
        if (batch.Tasks.Any(t => t.Status != "Verified"))
        {
            Stop(objective, "Blocked", "Continuous execution stopped with incomplete work. Inspect the draft; no automatic replanning.");
            return;
        }
        if (!CanContinue(project, objective, batch, now) ||
            objective.PlanningCyclesUsed >= objective.MaxPlanningCycles || RemainingAttempts(objective, batch) == 0)
        {
            Stop(objective, "Review required", "Verified work is ready for review. Authorization, cycle or coding limit reached; no automatic renewal.");
            return;
        }
        objective.PlanningState = "Reassessment queued";
        objective.PlanningDetail = "Prior cycle verified. Archer may reassess the same draft within the existing authorization.";
    }

    public static bool Claim(ManagedProject project, ProjectObjective objective, DevelopmentBatch batch, DateTimeOffset now)
    {
        if (objective.PlanningState != "Reassessment queued" || !CanContinue(project, objective, batch, now) ||
            objective.PlanningCyclesUsed >= objective.MaxPlanningCycles || RemainingAttempts(objective, batch) == 0)
            return false;
        objective.PlanningCyclesUsed++;
        objective.PlanningState = "Planning";
        objective.PlanningDetail = "One reassessment call reserved before external execution.";
        return true;
    }

    public static bool Append(ManagedProject project, ProjectObjective objective, DevelopmentBatch batch,
        IReadOnlyList<DevelopmentTask> tasks, int projectAttemptsRemaining, DateTimeOffset now)
    {
        if (!Authorized(objective, now) || !ProjectPolicy.ObjectiveReady(project, objective) ||
            project.Paused || !project.CumulativeWorkspaces || !batch.Continuous || !batch.Cumulative ||
            batch.ProjectId != project.Id || batch.ObjectiveId != objective.Id ||
            objective.PlanningState != "Planning" || objective.PlanningCyclesUsed != batch.PlanningCycles + 1 ||
            objective.PlanningCyclesUsed > objective.MaxPlanningCycles ||
            batch.Armed || batch.ExpiresAtUtc != objective.DelegatedUntilUtc ||
            batch.Tasks.Count == 0 || batch.Tasks.Any(t => t.Status != "Verified") ||
            !SandboxPolicy.IsHead(batch.WorkspaceHeadSha) || batch.Tasks.Last().Proposal?.PublishedHeadSha != batch.WorkspaceHeadSha ||
            tasks.Count is < 1 or > 3 || tasks.Any(t => t.Status != "Queued" || t.Attempts != 0 || t.Proposal is not null) ||
            batch.Tasks.Count + tasks.Count > 12)
            return false;
        var allowance = Math.Min(projectAttemptsRemaining, Math.Min(RemainingAttempts(objective, batch), tasks.Count * 2));
        if (allowance < tasks.Count) return false;
        batch.Tasks.AddRange(tasks);
        batch.PlanningCycles = objective.PlanningCyclesUsed;
        batch.AttemptsRemaining = allowance;
        batch.Armed = true;
        objective.PlanningState = "Batch created";
        objective.PlanningDetail = "Next ordered cycle queued on the same draft. Original deadline and total coding limit remain unchanged.";
        return true;
    }

    public static string History(DevelopmentBatch batch)
    {
        var lines = batch.Tasks.TakeLast(12).Select(t =>
            t.Status + ": " + t.Instruction + " | paths: " + string.Join(", ", t.Paths.Concat(t.NewPaths)) +
            " | head: " + (t.Proposal?.PublishedHeadSha ?? "none") + " | " + (t.Detail ?? ""));
        var text = string.Join("\n", lines);
        return text[..Math.Min(text.Length, 10000)];
    }
}
