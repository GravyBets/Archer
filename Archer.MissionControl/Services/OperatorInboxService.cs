using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// First unified event feed for Archer's Windows dashboard and future phone app.
/// Source-of-truth: locally recorded website incidents and mission task failures.
/// Does NOT perform pushes, phone calls, AI requests or external actions.
/// </summary>
public sealed class OperatorInboxService(JsonFileStore store)
{
    private const int MaxItems = 200;

    private static string Clip(string? value, int max)
    {
        var clean = (value ?? "").Trim();
        return clean.Length <= max ? clean : clean[..(max - 1)] + "…";
    }

    // Sync is idempotent: existing source IDs do not generate repeat alerts.
    public async Task SyncAsync(CancellationToken ct)
    {
        // Avoid unnecessary disk writes in the ordinary no-change case.
        var needsSync = await store.ReadAsync(state =>
        {
            var known = state.Notifications.Select(x => x.SourceKey)
                .ToHashSet(StringComparer.Ordinal);
            if (state.WebsiteIncidents.Take(100)
                .Any(i => !known.Contains("website:" + i.Id.ToString("N"))))
                return true;
            if (state.Missions.SelectMany(m => m.Tasks.Where(t => t.Status == "Failed")
                    .Select(t => "task:" + m.Id.ToString("N") + ":" + t.Id.ToString("N")))
                .Any(key => !known.Contains(key)))
                return true;
            // Detect a failure that was retried / a recovered site.
            return state.Notifications.Any(n =>
                n.ResolvedAtUtc is null &&
                ((n.Category == "Mission" && n.MissionId is { } missionId &&
                  !state.Missions.Any(m => m.Id == missionId &&
                    m.Tasks.Any(t => t.Status == "Failed" &&
                      n.SourceKey == "task:" + m.Id.ToString("N") + ":" + t.Id.ToString("N")))) ||
                 (n.Category == "Website" && n.Priority != "Information" &&
                  n.WebsiteMonitorId is { } monitorId &&
                  state.WebsiteMonitors.Any(m => m.Id == monitorId && m.Status == "Up"))));
        }, ct);

        if (!needsSync) return;

        await store.UpdateAsync(state =>
        {
            var now = DateTimeOffset.UtcNow;
            var known = state.Notifications.Select(x => x.SourceKey)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var incident in state.WebsiteIncidents.Take(100))
            {
                var key = "website:" + incident.Id.ToString("N");
                if (!known.Add(key)) continue;
                var outage = incident.Kind == "Outage";
                state.Notifications.Add(new OperatorNotification
                {
                    SourceKey = key,
                    Category = "Website",
                    // Three reachability failures merit review, but do not
                    // prove that payments or the entire service failed.
                    Priority = outage ? "UrgentReview" : "Information",
                    Title = Clip((outage ? "Website needs review: " : "Website recovered: ") +
                        incident.WebsiteName, 100),
                    Summary = Clip(incident.Message, 260),
                    OccurredAtUtc = incident.OccurredAtUtc,
                    WebsiteMonitorId = incident.WebsiteMonitorId,
                    ReadAtUtc = null,
                    AcknowledgedAtUtc = incident.AcknowledgedAtUtc
                });
            }

            foreach (var mission in state.Missions)
            foreach (var task in mission.Tasks.Where(t => t.Status == "Failed"))
            {
                var key = "task:" + mission.Id.ToString("N") + ":" + task.Id.ToString("N");
                if (!known.Add(key)) continue;
                state.Notifications.Add(new OperatorNotification
                {
                    SourceKey = key,
                    Category = "Mission",
                    Priority = "Attention",
                    Title = Clip("Task failed: " + task.Title, 100),
                    Summary = Clip("Mission: " + mission.Title +
                        ". Check the task details before retrying.", 260),
                    OccurredAtUtc = now, // first recorded in inbox, not a fabricated failure timestamp
                    MissionId = mission.Id
                });
            }

            // Mark old items as resolved when the source no longer reports the
            // same problem. Resolved records remain until bounded pruning.
            foreach (var notification in state.Notifications.Where(x => x.ResolvedAtUtc is null))
            {
                if (notification.Category == "Mission" && notification.MissionId is { } missionId)
                {
                    var stillFailed = state.Missions.Any(m => m.Id == missionId &&
                        m.Tasks.Any(t => t.Status == "Failed" &&
                            notification.SourceKey ==
                              "task:" + m.Id.ToString("N") + ":" + t.Id.ToString("N")));
                    if (!stillFailed) notification.ResolvedAtUtc = now;
                }
                else if (notification.Category == "Website" && notification.Priority == "UrgentReview"
                    && notification.WebsiteMonitorId is { } monitorId)
                {
                    // An actual successful check is evidence for recovery;
                    // an app shutdown or stale check is NOT evidence.
                    var recovered = state.WebsiteMonitors.Any(m =>
                        m.Id == monitorId && m.Status == "Up" &&
                        m.LastCheckedAtUtc is { } checkedAt &&
                        checkedAt >= notification.OccurredAtUtc);
                    if (recovered) notification.ResolvedAtUtc = now;
                }
            }

            state.Notifications = state.Notifications
                .OrderByDescending(n => n.OccurredAtUtc)
                .ThenByDescending(n => n.Id)
                .Take(MaxItems).ToList();
            return true;
        }, ct);
    }

    public async Task<object> GetAsync(CancellationToken ct)
    {
        await SyncAsync(ct);
        return await store.ReadAsync(state =>
        {
            var items = state.Notifications.OrderByDescending(x => x.OccurredAtUtc)
                .Take(100).ToArray();
            return (object)new
            {
                items,
                unreadCount = state.Notifications.Count(n => n.ReadAtUtc is null),
                needsReviewCount = state.Notifications.Count(n =>
                    n.Priority != "Information" && n.AcknowledgedAtUtc is null &&
                    n.ResolvedAtUtc is null),
                historyLimit = MaxItems,
                delivered = false,
                disclaimer = "Local event inbox only. Unverified website failures are not confirmed business outages. No push notification, SMS or calls are triggered by viewing or acknowledging an item."
            };
        }, ct);
    }

    public async Task<bool> MarkAsync(Guid id, bool acknowledge, CancellationToken ct)
    {
        // This API only changes local inbox flags, not mission task status,
        // website monitoring configuration or any external provider.
        return await store.UpdateAsync(state =>
        {
            var item = state.Notifications.FirstOrDefault(x => x.Id == id);
            if (item is null) return false;
            var now = DateTimeOffset.UtcNow;
            item.ReadAtUtc ??= now;
            if (acknowledge) item.AcknowledgedAtUtc ??= now;
            return true;
        }, ct);
    }
}

/// <summary>
/// Local periodic materialization means notifications also exist if the
/// dashboard is closed but Archer is still running. No outbound delivery.
/// </summary>
public sealed class OperatorInboxWorker(
    OperatorInboxService inbox, ILogger<OperatorInboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await inbox.SyncAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Local inbox sync failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(45), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
