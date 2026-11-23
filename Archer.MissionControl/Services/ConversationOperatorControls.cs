using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Small deterministic owner commands. They are reachable only from the
/// owner-local chat route or the independently authenticated companion
/// bridge, not AI outputs, imported drafts, or repository content.
/// </summary>
public static class ConversationOperatorControls
{
    private static readonly Regex Address = new(
        @"^(?:(?:hey\s+)?archer[,!:]?\s+)?(?:(?:please|can\s+you|could\s+you)\s+)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Command = new(
        @"^(?<verb>pause|stop|resume|unpause)\s+(?:(?<prefix>working\s+on|work\s+on|development\s+(?:on|for))\s+)?(?<name>[A-Za-z0-9 .'-]{2,95}?)(?<suffix>\s+(?:development|coding|project|work))?[.!?]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Status = new(
        @"^(?:(?:why\s+(?:isn't|is\s+not|can't|cannot)\s+(?<why>[A-Za-z0-9 .'-]{2,95}?)\s+(?:working|coding|starting))|(?:what(?:'s|\s+is)\s+(?:blocking|stopping)\s+(?:(?:work|coding)\s+on\s+)?(?<block>[A-Za-z0-9 .'-]{2,95}))|(?:is\s+(?<ready>[A-Za-z0-9 .'-]{2,95}?)\s+(?:ready\s+to\s+(?:code|work)|ready|working|configured|set\s+up))|(?:what\s+(?:does|do)\s+(?<needs>[A-Za-z0-9 .'-]{2,95}?)\s+need\s+to\s+start))[?.!]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Overnight = new(
        @"^(?:(?:give\s+me\s+)?(?:an?\s+)?(?:overnight\s+(?:report|update)|morning\s+(?:report|update|briefing))|what\s+(?:happened|did\s+you\s+(?:finish|do|get\s+done))\s+(?:while\s+i\s+(?:was\s+asleep|slept)|overnight))[!?.]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private sealed record Parsed(string Type, string? Name = null, bool Paused = false);

    private static string Body(string? message) =>
        Address.Replace((message ?? "").Trim(), "", 1).Trim();

    private static string Canonical(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s-]+", "");

    private static Parsed? Parse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 200) return null;
        var text = Body(message);
        if (Overnight.IsMatch(text)) return new("overnight");
        var command = Command.Match(text);
        if (command.Success)
        {
            var name = command.Groups["name"].Value.Trim();
            var qualified = command.Groups["prefix"].Success || command.Groups["suffix"].Success;
            var canonical = Canonical(name);
            // "stop listening", "stop talking", "pause microphone" and
            // "resume conversation" are NOT development commands.
            if (!qualified && canonical is not ("geoshift" or "archer")) return null;
            if (canonical == "archer" && !qualified) return null;
            return new("control", name, command.Groups["verb"].Value is "pause" or "stop" ||
                command.Groups["verb"].Value.Equals("pause", StringComparison.OrdinalIgnoreCase) ||
                command.Groups["verb"].Value.Equals("stop", StringComparison.OrdinalIgnoreCase));
        }
        var status = Status.Match(text);
        if (status.Success)
        {
            var name = new[] { "why", "block", "ready", "needs" }
                .Select(group => status.Groups[group].Value.Trim()).FirstOrDefault(v => v.Length > 0);
            if (name is not null && (Canonical(name) is "geoshift" or "archer" ||
                text.Contains("project", StringComparison.OrdinalIgnoreCase)))
                return new("status", name);
        }
        return null;
    }

    public static bool IsRequest(string? message) => Parse(message) is not null;

    private static ManagedProject? Project(ArcherState state, string name)
    {
        var matches = state.Projects.Where(p =>
            Canonical(p.Name) == Canonical(name)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string Answer(ArcherState state, string message, bool aiConfigured, DateTimeOffset now)
    {
        var parsed = Parse(message);
        if (parsed is null) return "No project command was recognized; nothing changed.";
        if (parsed.Type == "overnight") return OvernightReport(state, now);
        var project = Project(state, parsed.Name!);
        if (project is null)
            return Canonical(parsed.Name!) == "geoshift"
                ? "GeoShift isn't registered in this Archer instance yet. Ask me to register GeoShift; no coding has started."
                : "I couldn't identify one registered project by that name. Nothing changed.";
        if (parsed.Type == "status")
        {
            var checks = ProjectSetupReadiness.Evaluate(project, aiConfigured);
            var objective = project.Objectives.OrderByDescending(o => o.CreatedAtUtc).FirstOrDefault();
            var status = objective is null ? "No assignment is saved" :
                "Latest assignment: " + ConversationAssignments.PlainStatus(objective,
                    state.DevelopmentBatches.Where(b => b.ProjectId == project.Id && b.ObjectiveId == objective.Id));
            if (checks.Missing.Length > 0)
                return project.Name + " is not ready for coding. " + status + ". Setup needed: " +
                    string.Join("; ", checks.Missing.Take(5)) +
                    (checks.Missing.Length > 5 ? "; and " + (checks.Missing.Length - 5) + " more" : "") +
                    ". No new work was authorized.";
            return project.Name + " has passed the local setup checks. " + status +
                ". Independent GitHub CI still needs verification before any paid planning. No new work was authorized.";
        }
        var wasPaused = project.Paused;
        SetPaused(state, project, parsed.Paused);
        if (parsed.Paused)
            return wasPaused
                ? project.Name + " is already paused. No new work is authorized."
                : "Paused " + project.Name + " development and disarmed queued batches. An already running external request may finish.";
        return !wasPaused
            ? project.Name + " is already unpaused. No new coding authorization was given."
            : project.Name + " is unpaused. Previously stopped work remains disarmed; a new explicit assignment and normal checks are required.";
    }

    public static void SetPaused(ArcherState state, ManagedProject project, bool paused)
    {
        project.Paused = paused;
        if (!paused) return;
        foreach (var batch in state.DevelopmentBatches.Where(b => b.ProjectId == project.Id))
            batch.Armed = false;
        foreach (var objective in project.Objectives.Where(o =>
            o.PlanningState is "Delegated" or "Planning" or "Reassessment queued" ||
            (o.Continuous && o.PlanningState == "Batch created")))
        {
            objective.DelegatedUntilUtc = null;
            objective.PlanningState = objective.PlanningAttempted ? "Blocked" : "Not delegated";
            objective.PlanningDetail = "Project paused by owner.";
        }
    }

    public static string OvernightReport(ArcherState state, DateTimeOffset now)
    {
        var since = now.AddHours(-12);
        var finished = state.Missions.SelectMany(m => m.Tasks).Count(t =>
            t.Status == "Completed" && t.CompletedAtUtc is { } time && time >= since && time <= now);
        var submitted = state.DevelopmentBatches
            .SelectMany(b => b.Tasks.Select(t => (Batch: b, Task: t)))
            .Count(x => x.Task.Status == "Verified" &&
                x.Task.SubmittedAtUtc is { } submittedAt && submittedAt >= since && submittedAt <= now);
        var active = state.Projects.Sum(p => p.Objectives.Count(o => o.PlanningState == "Planning")) +
            state.DevelopmentBatches.Sum(b => b.Tasks.Count(t => t.Status is "Editing" or "Publishing" or "Testing"));
        var blocked = state.Projects.Sum(p => p.Objectives.Count(o => o.PlanningState == "Blocked")) +
            state.DevelopmentBatches.Sum(b => b.Tasks.Count(t => t.Status == "Blocked"));
        var alerts = state.Notifications.Count(n => n.OccurredAtUtc >= since && n.OccurredAtUtc <= now);
        var readyDrafts = state.DevelopmentBatches.Count(b => b.Tasks.Count > 0 &&
            b.Tasks.All(t => t.Status == "Verified"));
        return "Recorded in the last 12 hours: " + finished + " completed mission steps, " +
            submitted + " currently verified coding steps submitted in that window, and " +
            alerts + " new local alerts. Currently " + active + " development steps in progress, " +
            blocked + " recorded blockers, and " + readyDrafts + " draft batches ready for owner review. " +
            "These are local records, not proof of deployment or sales. No messages or calls were sent by this report.";
    }
}
