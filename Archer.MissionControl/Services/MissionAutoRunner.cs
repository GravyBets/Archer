using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Single-process, explicitly authorized research runner.
/// No new tasks, purchases, messaging, or recurring schedules.
/// </summary>
public sealed class MissionAutoRunner(
    JsonFileStore store,
    ArcherAiService ai,
    ILogger<MissionAutoRunner> logger) : BackgroundService
{
    private sealed record Claim(Guid MissionId, Guid TaskId, Mission Mission, MissionTask Task);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Archer automatic execution worker encountered an error."); }

            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RunOneAsync(CancellationToken ct)
    {
        var enabled = await store.ReadAsync(state => state.Missions.Any(m => m.AutoRunEnabled), ct);
        if (!enabled) return; // Avoid writing state when idle.

        var claim = await store.UpdateAsync<Claim?>(state =>
        {
            foreach (var mission in state.Missions.Where(m => m.AutoRunEnabled))
            {
                if (mission.AutoRunTasksRemaining <= 0 ||
                    mission.Status is "Archived" or "Completed" ||
                    mission.Tasks.Any(t => t.Status == "Failed"))
                {
                    Disarm(mission);
                    continue;
                }

                // Manual execution or another worker has claimed this mission.
                if (mission.Status == "Running") continue;

                var task = mission.Tasks.FirstOrDefault(t => t.Status == "Pending");
                if (task is null)
                {
                    Disarm(mission);
                    if (mission.Tasks.Count > 0 && mission.Tasks.All(t => t.Status == "Completed"))
                        mission.Status = "Completed";
                    continue;
                }

                mission.AutoRunTasksRemaining--; // Charge authorization before API call.
                mission.Status = "Running";
                mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
                task.Status = "Running";
                task.LastError = null;
                Record(state, mission.Id, $"Automatic task started: {task.Title}");
                return new Claim(mission.Id, task.Id, mission, task);
            }
            return null;
        }, ct);
        if (claim is null) return;

        try
        {
            var report = await ai.ExecuteTaskAsync(claim.Mission, claim.Task, ct);
            await store.UpdateAsync(state =>
            {
                var mission = state.Missions.Single(m => m.Id == claim.MissionId);
                var task = mission.Tasks.Single(t => t.Id == claim.TaskId);
                task.Status = "Completed";
                task.CompletedAtUtc = DateTimeOffset.UtcNow;
                mission.Artifacts.Add(new MissionArtifact
                {
                    TaskId = task.Id, Title = task.Title, Content = report
                });
                var complete = mission.Tasks.All(t => t.Status == "Completed");
                if (complete || mission.AutoRunTasksRemaining <= 0) Disarm(mission);
                mission.Status = complete ? "Completed" : "Ready";
                mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
                Record(state, mission.Id, $"Automatic task completed: {task.Title}", "Success");
                return true;
            }, CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Interrupted tasks are returned to Pending and disarmed on next startup.
            throw;
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Archer automatic task failed.");
            await store.UpdateAsync(state =>
            {
                var mission = state.Missions.Single(m => m.Id == claim.MissionId);
                var task = mission.Tasks.Single(t => t.Id == claim.TaskId);
                task.Status = "Failed";
                task.LastError = error.Message;
                Disarm(mission);
                mission.Status = "Paused";
                mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
                Record(state, mission.Id, $"Automatic task failed: {task.Title}", "Warning");
                return true;
            }, CancellationToken.None);
        }
    }

    private static void Disarm(Mission mission)
    {
        mission.AutoRunEnabled = false;
        mission.AutoRunTasksRemaining = 0;
    }

    private static void Record(ArcherState state, Guid missionId, string message, string level = "Info")
    {
        state.Activity.Insert(0, new ActivityEntry
        {
            MissionId = missionId, Message = message, Level = level
        });
        if (state.Activity.Count > 250)
            state.Activity.RemoveRange(250, state.Activity.Count - 250);
    }
}
