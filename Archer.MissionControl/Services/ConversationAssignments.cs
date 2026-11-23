using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

// Only direct owner input reaches this parser. Model responses, repo content,
// research reports and imported phone drafts are not command sources.
public static class ConversationAssignments
{
    private static readonly Regex Prefix = new(
        @"^(?:(?:hey\s+)?archer[,!:]?\s+)?(?:(?:please|i\s+want\s+you\s+to|go\s+ahead\s+and|start|can\s+you)\s+)*(?<action>work\s+on|assign\s+work\s+to|develop|fix|improve|update|build)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public sealed record Command(string Project, string Objective);
    public sealed record Result(string Answer, Guid? ObjectiveId = null, bool Accepted = false);

    public static bool LooksLikeAssignment(string text) => Prefix.IsMatch(text.Trim());

    public static Command? Parse(string text, IEnumerable<ManagedProject> projects)
    {
        var prefix = Prefix.Match(text.Trim());
        if (!prefix.Success) return null;
        var remainder = text.Trim()[prefix.Length..];
        foreach (var name in projects.Select(p => p.Name).OrderByDescending(n => n.Length))
        {
            if (!remainder.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            var tail = remainder[name.Length..];
            // A named project and a distinct requested result are mandatory.
            var delimiter = Regex.Match(tail, @"^(?:\s*[:;,–—]\s*|\s+(?:and|to)\s+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (delimiter.Success)
                return new(name, tail[delimiter.Length..].Trim());
            var possessive = Regex.Match(tail, @"^['’]s\s+(?<detail>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var verb = prefix.Groups["action"].Value.ToLowerInvariant();
            if (possessive.Success && (verb is "fix" or "improve" or "update" or "build" or "develop"))
                return new(name, char.ToUpperInvariant(verb[0]) + verb[1..] + " " +
                    possessive.Groups["detail"].Value.Trim());
        }
        return null;
    }

    public static Result Assign(ArcherState state, string input, Func<ManagedProject, bool> canArm,
        DateTimeOffset now, bool resumeAfterRestart = false)
    {
        var command = Parse(input, state.Projects);
        if (command is null)
            return new("Tell me which registered project and what result you want. For example, Work on Archer and improve the inbox.");
        if (command.Objective.Length is < 15 or > 1000 || command.Objective.Any(char.IsControl))
            return new("Please describe one concrete result in 15 to 1,000 characters.");
        var matches = state.Projects.Where(p => string.Equals(p.Name, command.Project, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) return new("I need one unambiguous registered project before I can start.");
        var project = matches[0];
        if (!canArm(project) || project.Paused)
            return new(project.Name + " is paused or needs setup. I haven't started paid work.");
        var remaining = project.DailyAttemptLimit - ProjectPolicy.AttemptsToday(state, project.Id, now);
        if (remaining <= 0) return new("Today's authorized coding allowance for " + project.Name + " is used up.");
        // A repeated voice transcript must not create another paid assignment.
        var existing = project.Objectives.LastOrDefault(o => string.Equals(o.Description.Trim(),
            command.Objective, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return new("That assignment is already recorded for " + project.Name + ". Its status is " +
                PlainStatus(existing, state.DevelopmentBatches.Where(b => b.ObjectiveId == existing.Id && b.ProjectId == project.Id)) +
                ". I haven't duplicated it.", existing.Id);
        if (project.Objectives.Count >= 50 || state.DevelopmentBatches.Count >= 100)
            return new("The project history limit has been reached. No paid work started.");
        var objective = new ProjectObjective
        {
            Description = command.Objective, CreatedAtUtc = now, DelegatedUntilUtc = now.AddHours(24),
            ResumeDelegationAfterRestart = resumeAfterRestart, MaxPlanningCycles = 1,
            MaxCodingAttempts = Math.Min(6, remaining), PlanningState = "Delegated",
            PlanningDetail = "Direct owner conversation assignment. One bounded planning cycle, original 24-hour deadline; draft only."
        };
        project.Objectives.Add(objective);
        return new("I'll work on " + project.Name + ": " + command.Objective +
            " I'll prepare and check the changes, then bring them to you for review.", objective.Id, true);
    }

    public static bool IsStatusRequest(string text)
    {
        var normalized = Regex.Replace(text.Trim().ToLowerInvariant(), @"[?.!]+$", "");
        return normalized is "what are you working on" or "what are you working on right now" or
            "how is your work going" or "what have you finished" or "development status" or "give me a development update";
    }

    public static string Status(ArcherState state)
    {
        var lines = new List<string>();
        foreach (var p in state.Projects.OrderByDescending(p => p.Priority))
        foreach (var o in p.Objectives.TakeLast(5).Reverse())
        {
            if (lines.Count >= 6) break;
            lines.Add(p.Name + ": " + PlainStatus(o, state.DevelopmentBatches.Where(b => b.ProjectId == p.Id && b.ObjectiveId == o.Id)) +
                " — " + o.Description);
        }
        lines.AddRange(ConversationResearch.Status(state));
        return lines.Count == 0 ? "I don't have any research or development assignments yet." :
            "Here's my current work:\n" + string.Join("\n", lines);
    }

    public static string PlainStatus(ProjectObjective o, IEnumerable<DevelopmentBatch> related)
    {
        if (o.AcceptedAtUtc is not null) return "accepted";
        var batch = related.LastOrDefault();
        if (batch is not null)
        {
            if (batch.Tasks.Any(t => t.Status == "Blocked")) return "needs attention";
            if (batch.Tasks.Count > 0 && batch.Tasks.All(t => t.Status == "Verified"))
                return o.Continuous && o.PlanningState is ("Planning" or "Reassessment queued" or "Batch created")
                    ? "reassessing" : "ready for review";
            if (batch.Tasks.Any(t => t.Status == "Testing")) return "checking work";
            if (batch.Tasks.Any(t => t.Status is "Editing" or "Publishing")) return "working";
            return batch.Armed ? "queued" : "paused or awaiting approval";
        }
        return o.PlanningState switch
        {
            "Planning" => "planning",
            "Delegated" => "queued",
            "Blocked" => "needs attention",
            _ => "saved"
        };
    }
}


/// <summary>
/// Bounded owner-requested presentation preferences. Changing the arrangement
/// of the orb and conversation does not edit source code or grant code access.
/// Commands must specify both widgets and a clear relationship; discussion
/// and capability questions cannot modify the page.
/// </summary>
public static class ConversationHomeLayout
{
    public const string Centered = "centered";
    public const string Split = "split";

    private static readonly Regex Directive = new(
        @"^(?:(?:hey\s+)?archer[,!:]?\s+)?(?:(?:please|can\s+you|could\s+you|i\s+want\s+you\s+to|go\s+ahead\s+and)\s+)?(?:put|move|center|centre|adjust|change|rearrange|redesign|set|switch|fix|update)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex Orb = new(@"\borb\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Chat = new(@"\b(?:chat|conversation)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Center = new(@"\b(?:center|centre|middle)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Under = new(@"\b(?:under|underneath|below|beneath)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Beside = new(@"\b(?:beside|alongside|side[\s-]+by[\s-]+side|next\s+to|two[\s-]+columns)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string? Requested(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var text = message.Trim();
        if (text.Length > 1000 || !Directive.IsMatch(text) ||
            !Orb.IsMatch(text) || !Chat.IsMatch(text))
            return null;
        if (Center.IsMatch(text) && Under.IsMatch(text) && !Beside.IsMatch(text))
            return Centered;
        if (Beside.IsMatch(text) && !Under.IsMatch(text))
            return Split;
        return null;
    }

    public static string Apply(ArcherState state, string layout)
    {
        if (layout is not (Centered or Split))
            return "I couldn't identify a supported layout. Nothing changed.";
        var already = state.HomeLayout == layout;
        state.HomeLayout = layout;
        return layout == Centered
            ? already
                ? "The orb is already centered, with the chat underneath."
                : "Done. I've centered the orb and moved the chat underneath it."
            : already
                ? "The orb and chat are already side by side."
                : "Done. I've put the orb beside the chat.";
    }
}
