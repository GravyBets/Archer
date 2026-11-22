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
    // Reviewable coding drafts are local until explicitly approved for publication.
    public List<CodeProposal> CodeProposals { get; set; } = [];
    public List<DevelopmentBatch> DevelopmentBatches { get; set; } = [];
    public CommunicationPreferences CommunicationPreferences { get; set; } = new();
    public List<CommunicationAttempt> CommunicationAttempts { get; set; } = [];
    // Persistent source-grounded local inbox. Not phone notifications or an APNs delivery queue.
    public List<OperatorNotification> Notifications { get; set; } = [];
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
public sealed record ChatRequest(string? Message, bool VoiceMode = false);
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

public sealed class CodeProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Repository { get; set; } = "";
    public string BaseBranch { get; set; } = "";
    public string Title { get; set; } = "";
    public string Request { get; set; } = "";
    public string Summary { get; set; } = "";
    public string TestRecommendations { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public string? PullRequestUrl { get; set; }
    public string? BranchName { get; set; }
    public List<CodeProposalFile> Files { get; set; } = [];
}

public sealed class CodeProposalFile
{
    public string Path { get; set; } = "";
    public string ExpectedBlobSha { get; set; } = "";
    public string OriginalContent { get; set; } = "";
    public string ProposedContent { get; set; } = "";
}

public sealed record DraftCodeRequest(string? Repository, List<string>? Paths, string? Instruction);
public sealed record PublishCodeRequest(bool Confirm);

public sealed class CommunicationPreferences
{
    // Disabled by default. Opt in only after Twilio credentials and recipient are verified.
    public bool DailySmsEnabled { get; set; }
    public bool UrgentCallsEnabled { get; set; }
}

public sealed class CommunicationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public string Channel { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public string Status { get; set; } = "Reserved";
    public string? ProviderReference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record UpdateCommunicationPreferencesRequest(
    bool DailySmsEnabled, bool UrgentCallsEnabled, bool ConfirmCharges);
public sealed record ConfirmCommunicationRequest(bool Confirm);
public sealed record MobilePresenceRequest(
    string? Activity, string? PlaceLabel, int DurationMinutes, bool ShareWithAi);

public sealed class OperatorNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Stable dedup key comes from Archer-owned incident/task IDs, not user content.
    public string SourceKey { get; set; } = "";
    public string Category { get; set; } = "Mission";
    public string Priority { get; set; } = "Attention";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public Guid? MissionId { get; set; }
    public Guid? WebsiteMonitorId { get; set; }
}

public sealed class DevelopmentBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Objective { get; set; } = "";
    public bool Armed { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public int AttemptsRemaining { get; set; }
    public List<DevelopmentTask> Tasks { get; set; } = [];
}
public sealed class DevelopmentTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Instruction { get; set; } = "";
    public List<string> Paths { get; set; } = [];
    public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public CodeProposal? Proposal { get; set; }
    public List<string> PriorPullRequests { get; set; } = [];
}
public sealed record DevelopmentPlanRequest(string? Objective, bool Confirm);
public sealed record DevelopmentArmRequest(bool Confirm);
