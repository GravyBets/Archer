namespace Archer.MissionControl.Domain;

public sealed class ArcherState
{
    public List<Mission> Missions { get; set; } = [];
    public List<ActivityEntry> Activity { get; set; } = [];
    public List<ConversationEntry> Conversation { get; set; } = [];
    // A single, optional presence snapshot; no location/activity history.
    public OwnerPresence? Presence { get; set; }
    public List<WebsiteMonitor> WebsiteMonitors { get; set; } = [];
    public List<WebsiteIncident> WebsiteIncidents { get; set; } = [];
    // Notification routing preferences are preview-only until real delivery is explicitly configured.
    public BriefingPreferences BriefingPreferences { get; set; } = new();
}

public sealed class Mission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "New";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    // Explicitly authorized, limited to three attempts at a time; reset on restart.
    public bool AutoRunEnabled { get; set; } = false;
    public int AutoRunTasksRemaining { get; set; } = 0;
    public List<MissionTask> Tasks { get; set; } = [];
    public List<MissionArtifact> Artifacts { get; set; } = [];
}

public sealed class MissionTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "research";
    public string Status { get; set; } = "Pending";
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class MissionArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ActivityEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MissionId { get; set; }
    public string Message { get; set; } = "";
    public string Level { get; set; } = "Info";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ConversationEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record CreateMissionRequest(string? Title, string? Objective);
public sealed record ChatRequest(string? Message);
public sealed record RunMissionRequest(int MaxSteps = 1);

public sealed class PlanDraft
{
    public string Summary { get; set; } = "";
    public List<PlanTaskDraft> Tasks { get; set; } = [];
}

public sealed class PlanTaskDraft
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "research";
}

public sealed record SetAutoRunRequest(bool Enabled);

// Explicit source selections for read-only Developer Workspace previews.
public sealed record ReadSourceFilesRequest(string? Repository, List<string>? Paths);

public sealed class OwnerPresence
{
    public string Activity { get; set; } = "Available";
    public string PlaceLabel { get; set; } = "";
    // One-time, approximate location only (~1 km, no route history).
    public double? ApproxLatitude { get; set; }
    public double? ApproxLongitude { get; set; }
    public bool ShareWithAi { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
}

public sealed record UpdatePresenceRequest(
    string? Activity, string? PlaceLabel, int DurationMinutes, bool ShareWithAi,
    double? Latitude, double? Longitude);

public sealed class WebsiteMonitor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = false;
    public string Status { get; set; } = "Not monitored";
    public int ConsecutiveFailures { get; set; }
    public int? LastHttpStatus { get; set; }
    public long? LastLatencyMs { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastCheckedAtUtc { get; set; }
}

public sealed class WebsiteIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WebsiteMonitorId { get; set; }
    public string WebsiteName { get; set; } = "";
    public string Kind { get; set; } = "Outage";
    public string Message { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
}

public sealed record CreateWebsiteMonitorRequest(string? Name, string? Url);
public sealed record SetWebsiteMonitorEnabledRequest(bool Enabled);

public sealed class BriefingPreferences
{
    // Local time for the eventual DAILY briefing. No scheduler or phone delivery yet.
    public string TimeZoneId { get; set; } = "America/Chicago";
    public int DailyHourLocal { get; set; } = 8;
    public int QuietStartHourLocal { get; set; } = 22;
    public int QuietEndHourLocal { get; set; } = 7;
    // Policy preview only; DOES NOT activate outbound calls.
    public bool CriticalOverrideQuietHours { get; set; } = false;
}

public sealed record UpdateBriefingPreferencesRequest(
    string? TimeZoneId, int DailyHourLocal, int QuietStartHourLocal,
    int QuietEndHourLocal, bool CriticalOverrideQuietHours);
