using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public static class DevelopmentBatchPolicy
{
    public static void Recover(DevelopmentBatch batch, DateTimeOffset now)
    {
        if (!batch.ResumeAfterRestart || batch.ExpiresAtUtc is null || batch.ExpiresAtUtc <= now ||
            batch.Tasks.Any(t => t.Status is "Editing" or "Publishing" or "Blocked"))
            batch.Armed = false;
        foreach (var task in batch.Tasks.Where(t => t.Status is "Editing" or "Publishing"))
        {
            task.Status = "Blocked";
            task.Detail = "Interrupted before a recorded result. Inspect any GitHub branch/PR before creating a new batch; no automatic retry.";
        }
    }

    public static bool Claim(DevelopmentBatch batch, DevelopmentTask task, DateTimeOffset now)
    {
        if (!batch.Armed || batch.ExpiresAtUtc is null || batch.ExpiresAtUtc <= now ||
            batch.AttemptsRemaining <= 0 || task.Attempts >= 2 ||
            task.Status is not ("Queued" or "Repair queued") ||
            batch.Tasks.FirstOrDefault(t => t.Status != "Verified") != task)
            return false;
        batch.AttemptsRemaining--;
        task.Attempts++;
        task.Status = "Editing";
        return true;
    }

    public static void CompleteCi(DevelopmentBatch batch, DevelopmentTask task, string status, string detail)
    {
        if (task.Status != "Testing" || status == "Testing") return;
        task.Detail = detail;
        task.Status = status == "Failed" && task.Attempts < 2 && batch.AttemptsRemaining > 0
            ? "Repair queued" : status == "Verified" ? "Verified" : "Blocked";
        if (task.Status == "Blocked" || batch.Tasks.All(t => t.Status == "Verified"))
            batch.Armed = false;
    }
}
