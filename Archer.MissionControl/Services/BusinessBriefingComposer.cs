using System.Globalization;
using System.Text;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Deterministic, no-AI briefing from locally observed Archer data only.
/// Never fabricates sales or customer metrics. Alerts are previews, not deliveries.
/// </summary>
public sealed class BusinessBriefingComposer(JsonFileStore store, ApiUsageLedger usage)
{
    public sealed record AlertPreview(
        string Id, string Priority, string Title, string Detail, string Source,
        string RecommendedDelivery, string Reason, bool DuringQuietHours);

    public sealed record BriefingPreview(
        DateTimeOffset GeneratedAtUtc,
        string LocalDateTime,
        string TimeZoneId,
        bool QuietHoursNow,
        string DeliveryStatus,
        string BriefingText,
        object Metrics,
        IReadOnlyList<AlertPreview> Alerts);

    private static readonly HashSet<string> AllowedTimeZones =
    [
        "America/Chicago", "America/New_York", "America/Denver",
        "America/Los_Angeles", "America/Phoenix", "UTC"
    ];

    public static void ValidatePreferences(BriefingPreferences p)
    {
        if (!AllowedTimeZones.Contains(p.TimeZoneId) ||
            p.DailyHourLocal is < 0 or > 23 ||
            p.QuietStartHourLocal is < 0 or > 23 ||
            p.QuietEndHourLocal is < 0 or > 23)
            throw new ArgumentException(
                "Select a supported timezone and hours from 00 through 23.");
    }

    public static IReadOnlyList<string> SupportedTimeZones =>
        AllowedTimeZones.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static bool DuringQuietHours(BriefingPreferences prefs, int hour)
    {
        var from = prefs.QuietStartHourLocal;
        var until = prefs.QuietEndHourLocal;
        if (from == until) return false; // Same start/end disables quiet hours.
        return from < until
            ? hour >= from && hour < until
            : hour >= from || hour < until;
    }

    private static string RecommendedChannel(string priority, bool quiet, BriefingPreferences prefs)
    {
        // Advisory only. The app has no delivery integration.
        if (priority == "Critical")
        {
            if (quiet && !prefs.CriticalOverrideQuietHours)
                return "Hold call during quiet hours; dashboard review";
            return "Urgent call after verified provider setup";
        }
        if (priority == "Attention")
            return quiet ? "Next daily briefing after quiet hours" : "Text after provider setup";
        return "Next daily briefing";
    }

    public async Task<BriefingPreview> BuildAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var state = await store.ReadAsync(x => x, ct);
        var prefs = state.BriefingPreferences ?? new BriefingPreferences();
        ValidatePreferences(prefs);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(prefs.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var quiet = DuringQuietHours(prefs, local.Hour);
        var cutoff = now.AddHours(-24);
        var totals = await usage.BriefingTotalsAsync(now, ct);

        var enabled = state.WebsiteMonitors.Where(x => x.Enabled).ToArray();
        var up = enabled.Count(x => x.Status == "Up");
        var degraded = enabled.Count(x => x.Status == "Degraded");
        var down = enabled.Count(x => x.Status == "Down");
        var never = enabled.Count(x => !x.LastCheckedAtUtc.HasValue);
        var outages24 = state.WebsiteIncidents.Count(i =>
            i.Kind == "Outage" && i.OccurredAtUtc >= cutoff && i.OccurredAtUtc <= now);
        var recovered24 = state.WebsiteIncidents.Count(i =>
            i.Kind == "Recovered" && i.OccurredAtUtc >= cutoff && i.OccurredAtUtc <= now);
        var failedTasks = state.Missions.SelectMany(x => x.Tasks).Count(x => x.Status == "Failed");
        var completed24 = state.Missions.SelectMany(x => x.Tasks).Count(x =>
            x.Status == "Completed" && x.CompletedAtUtc is { } finished &&
            finished >= cutoff && finished <= now);
        var activeMissions = state.Missions.Count(x => x.Status is "Running" or "Ready");

        var alerts = new List<AlertPreview>();
        foreach (var site in enabled.Where(x => x.Status is "Down" or "Degraded").Take(10))
        {
            var critical = site.Status == "Down";
            var priority = critical ? "Critical" : "Attention";
            alerts.Add(new AlertPreview(site.Id.ToString(), priority,
                $"{site.Name}: {site.Status.ToLowerInvariant()}",
                $"Last checked: {(site.LastCheckedAtUtc?.ToString("u") ?? "never")}. " +
                $"Failed checks: {site.ConsecutiveFailures}; " +
                $"HTTP: {(site.LastHttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "unavailable")}.",
                "Website HTTPS check", RecommendedChannel(priority, quiet, prefs),
                critical
                    ? "Three failed reachability checks; verify a real outage before calling."
                    : "One or two failed checks; avoid treating a transient failure as an outage.",
                quiet));
        }

        foreach (var item in state.Missions.SelectMany(m =>
                     m.Tasks.Where(t => t.Status == "Failed")
                         .Select(t => (Mission: m, Task: t))).Take(10))
        {
            alerts.Add(new AlertPreview(item.Task.Id.ToString(), "Attention",
                $"Mission task failed: {item.Task.Title}",
                $"Mission: {item.Mission.Title}. Review the task before retrying.",
                "Local mission task", RecommendedChannel("Attention", quiet, prefs),
                "A failed task requires review; it is not automatically a business emergency.",
                quiet));
        }

        var unacknowledged = state.WebsiteIncidents
            .Where(i => i.AcknowledgedAtUtc is null).Take(15).ToArray();

        // Include recovered, unacknowledged items without inventing current outages.
        foreach (var item in unacknowledged.Where(x => x.Kind == "Recovered").Take(5))
        {
            alerts.Add(new AlertPreview(item.Id.ToString(), "Information",
                $"Recovered: {item.WebsiteName}",
                item.Message, "Website incident",
                RecommendedChannel("Information", quiet, prefs),
                "The website responded again; informational only.", quiet));
        }

        // Short, copyable briefing for a future consent-based SMS/voice integration.
        var body = new StringBuilder();
        body.AppendLine($"Archer briefing — {local:ddd, MMM d yyyy h:mm tt} ({prefs.TimeZoneId})");
        body.AppendLine("Window: last 24 hours unless noted.");
        body.AppendLine($"Missions: {activeMissions} active/ready; {completed24} tasks completed; {failedTasks} currently failed.");
        body.AppendLine($"Websites: {enabled.Length} checked sites; {up} up, {degraded} degraded, {down} down; {never} not checked yet.");
        body.AppendLine($"Availability incidents: {outages24} outages and {recovered24} recoveries recorded.");
        body.AppendLine("AI cost: $" +
            totals.EstimatedCostLast24HoursUsd.ToString("0.0000", CultureInfo.InvariantCulture) +
            " estimated in 24h; $" +
            totals.EstimatedCostMonthUsd.ToString("0.0000", CultureInfo.InvariantCulture) +
            $" month-to-date (UTC); {totals.UnpricedRequestsLast24Hours} unpriced requests in 24h.");
        body.AppendLine("Revenue / payments / conversions: Not connected. No figures available.");
        if (alerts.Count > 0)
        {
            body.AppendLine($"Attention: {alerts.Count} active/recorded item(s).");
            foreach (var alert in alerts.Take(3))
                body.AppendLine($"- {alert.Priority}: {alert.Title}.");
        }
        else
            body.AppendLine("No current alerts from connected local sources.");
        body.Append("DRAFT ONLY. SMS and calling are not connected. Archer must be running for website checks.");

        return new BriefingPreview(now,
            local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            prefs.TimeZoneId, quiet, "preview_only",
            body.ToString(),
            new
            {
                missions = new { active = activeMissions, tasksCompleted24Hours = completed24, failedTasks },
                websites = new
                {
                    configured = state.WebsiteMonitors.Count, enabled = enabled.Length,
                    up, degraded, down, neverChecked = never,
                    outagesRecorded24Hours = outages24, recoveriesRecorded24Hours = recovered24
                },
                usage = totals,
                revenueAvailable = false,
                paymentMetricsAvailable = false,
                customerMetricsAvailable = false,
                deliveryConnected = false
            }, alerts);
    }
}
