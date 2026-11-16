namespace Archer.MissionControl.Domain;

public sealed class ArcherState
{
    public List<Mission> Missions { get; set; } = [];
    public List<ActivityEntry> Activity { get; set; } = [];
    public List<ConversationEntry> Conversation { get; set; } = [];
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
