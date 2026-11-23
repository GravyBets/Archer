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

    public static bool LooksLikeAssignment(string text) =>
        Prefix.IsMatch(text.Trim()) || GeoShiftConversationSetup.IsRegistrationRequest(text);

    public static Command? Parse(string text, IEnumerable<ManagedProject> projects)
    {
        var prefix = Prefix.Match(text.Trim());
        if (!prefix.Success) return null;
        var remainder = text.Trim()[prefix.Length..];
        foreach (var name in projects.Select(p => p.Name).OrderByDescending(n => n.Length))
        foreach (var spokenName in name.Equals("GeoShift", StringComparison.OrdinalIgnoreCase)
                     ? new[] { name, "Geo Shift" } : new[] { name })
        {
            if (!remainder.StartsWith(spokenName, StringComparison.OrdinalIgnoreCase)) continue;
            var tail = remainder[spokenName.Length..];
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
        // A direct owner request may register the known GeoShift repo and save
        // one objective, but cannot silently authorize paid coding or writes.
        var setup = GeoShiftConversationSetup.TryRegister(state, input);
        if (setup is not null) return setup;
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
        {
            if (GeoShiftConversationSetup.IsGeoShift(project) && existing.AcceptedAtUtc is null &&
                !existing.PlanningAttempted && existing.PlanningState == "Not delegated")
            {
                // A second explicit owner command after completing setup can
                // activate the saved intent. No work starts before CanArm.
                existing.PlanningState = "Delegated";
                existing.DelegatedUntilUtc = now.AddHours(24);
                existing.MaxPlanningCycles = 1;
                existing.MaxCodingAttempts = Math.Min(6, remaining);
                existing.PlanningDetail = "Owner reauthorized the saved GeoShift objective in conversation; draft only.";
                return new("GeoShift is now queued for planning. I'll prepare a reviewable draft, not merge or deploy it.", existing.Id, true);
            }
            return new("That assignment is already recorded for " + project.Name + ". Its status is " +
                PlainStatus(existing, state.DevelopmentBatches.Where(b => b.ObjectiveId == existing.Id && b.ProjectId == project.Id)) +
                ". I haven't duplicated it.", existing.Id);
        }
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

    // A small set of non-privileged, reversible presentation controls.
    // Do not treat general questions or arbitrary speech as code commands.
    private static readonly Regex Directive = new(
        @"^(?:(?:hey\s+)?archer[,!:]?\s+)?(?:(?:(?:yeah|yes|yep|definitely|right|sure)[,!\s]+){1,4})?(?:(?:please|can\s+you|could\s+you|i\s+want\s+you\s+to|go\s+ahead\s+and)\s+)?(?:put|move|center|centre|adjust|change|rearrange|redesign|set|switch|fix|update)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex Orb = new(@"\borb\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex MisheardOrb = new(@"\bword\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Chat = new(@"\b(?:chat|conversation)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Center = new(@"\b(?:center(?:ed)?|centre(?:d)?|middle)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Under = new(@"\b(?:under|underneath|below|beneath)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Beside = new(@"\b(?:beside|alongside|side[\s-]+by[\s-]+side|next\s+to|two[\s-]+columns)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex StatusQuestion = new(
        @"^(?:(?:hey\s+)?archer[,!:]?\s+)?(?:where\s+is|is|are|did|have|what(?:'s|\s+is)|can\s+you\s+(?:actually|really)|why\s+(?:can't|cant))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Thanks = new(
        @"^(?:(?:oh|ah|well|yeah|yes|finally|jesus|christ|is|for)\s+){0,5}(?:thanks|thank\s+you|cheers)[!. ]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string? Requested(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var text = message.Trim();
        if (text.Length > 1000 || !Directive.IsMatch(text) || !Chat.IsMatch(text)) return null;

        // Windows dictation sometimes transcribes "orb" as "word".
        // Only accept that variant with a clear center + below request,
        // never as a free-form or ambiguous change instruction.
        var hasOrb = Orb.IsMatch(text);
        var hasMisheardOrb = !hasOrb && MisheardOrb.IsMatch(text);
        if (!hasOrb && !hasMisheardOrb) return null;
        if (Center.IsMatch(text) && Under.IsMatch(text) && !Beside.IsMatch(text))
            return Centered;
        if (hasOrb && Beside.IsMatch(text) && !Under.IsMatch(text))
            return Split;
        return null;
    }

    public static bool IsStatusQuestion(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var text = message.Trim();
        return text.Length <= 220 && StatusQuestion.IsMatch(text) &&
            (Orb.IsMatch(text) || (text.Contains("layout", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("landing page", StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsAcknowledgement(string? message) =>
        !string.IsNullOrWhiteSpace(message) && message.Length <= 100 && Thanks.IsMatch(message.Trim());

    public static bool IsLocallyHandled(string? message) =>
        Requested(message) is not null || IsStatusQuestion(message) || IsAcknowledgement(message);

    public static string Answer(ArcherState state, string message)
    {
        var requested = Requested(message);
        if (requested is not null) return Apply(state, requested);
        if (IsStatusQuestion(message))
            return state.HomeLayout == Split
                ? "The orb is beside the chat right now. That's the saved layout."
                : "The orb is centered, and the chat is underneath it. That's the saved layout.";
        if (IsAcknowledgement(message))
        {
            var recent = state.Conversation.LastOrDefault(e => e.Role == "assistant")?.Content ?? "";
            var justChangedLayout = recent is "Done. I've centered the orb and moved the chat underneath it." or
                "Done. I've put the orb beside the chat." or
                "The orb is already centered, with the chat underneath." or
                "The orb and chat are already side by side.";
            return justChangedLayout
                ? "You're welcome. Finally, the furniture's where it belongs."
                : "You're welcome.";
        }
        return "I didn't change the layout. Tell me where you'd like the orb and chat.";
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


/// <summary>
/// One narrow local conversational onboarding path for the owner's known
/// GeoShift repository. This only saves registry metadata and an unarmed
/// objective. It never creates credentials, arms code work, merges or deploys.
/// The owner must separately configure scoped repository access and repeat the
/// explicit assignment after the worker's normal authorization gates pass.
/// </summary>
public static class GeoShiftConversationSetup
{
    public const string Repository = "GravyBets/GeoShift";
    public static bool IsGeoShift(ManagedProject project) =>
        project.Name.Equals("GeoShift", StringComparison.OrdinalIgnoreCase) &&
        project.Repository.Equals(Repository, StringComparison.OrdinalIgnoreCase);

    private static readonly Regex Register = new(
        @"^(?:(?:hey\s+)?archer[,!:]?\s+)?(?:(?:please|can\s+you|could\s+you)\s+)?(?:register|connect|add)\s+(?:the\s+)?geo\s*shift(?:\s+project)?[.!?]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool IsRegistrationRequest(string? input) =>
        !string.IsNullOrWhiteSpace(input) && input.Length <= 150 && Register.IsMatch(input.Trim());

    // Grounded in the real GeoShift repository and its checked-in
    // .github/workflows/archer-ci.yml: job "GeoShift build and tests".
    // No new files, secrets, workflows, installer code or deployment rights.
    public static RegisterProjectRequest Profile() =>
        new("GeoShift", Repository, ".NET / Blazor",
            ["GeoShift.Core", "GeoShift.Web/Components/Pages", "GeoShift.Web/wwwroot"],
            "archer-ci.yml", "GeoShift build and tests",
            ["Build Web", "Run core regression tests"],
            Priority: 4, DailyAttemptLimit: 6,
            AllowNewSourceFiles: false, CumulativeWorkspaces: true);

    public static ConversationAssignments.Result? TryRegister(ArcherState state, string input)
    {
        var explicitRegistration = IsRegistrationRequest(input);
        // Use the same canonical explicit-command parser as established
        // assignments, including the speech variant "Geo Shift".
        var project = ProjectPolicy.Register(Profile());
        var command = explicitRegistration ? null : ConversationAssignments.Parse(input, [project]);
        if (!explicitRegistration && (command is null ||
            !command.Project.Equals("GeoShift", StringComparison.OrdinalIgnoreCase) ||
            command.Objective.Length is < 15 or > 1000 || command.Objective.Any(char.IsControl)))
            return null;
        var already = state.Projects.Any(p =>
            p.Name.Equals("GeoShift", StringComparison.OrdinalIgnoreCase) ||
            p.Repository.Equals(Repository, StringComparison.OrdinalIgnoreCase));
        if (already)
        {
            if (state.Projects.Any(p =>
                (p.Name.Equals("GeoShift", StringComparison.OrdinalIgnoreCase) ||
                 p.Repository.Equals(Repository, StringComparison.OrdinalIgnoreCase)) && !IsGeoShift(p)))
                return new("GeoShift's registered name or repository conflicts with the verified project. I haven't changed or authorized anything.");
            return explicitRegistration
                ? new("GeoShift is already registered. Coding still needs separately authorized project access.")
                : null;
        }
        if (state.Projects.Count >= 20)
            return new("The project registry is full; I haven't changed it.");
        state.Projects.Add(project);
        if (command is not null)
            project.Objectives.Add(new ProjectObjective
            {
                Description = command.Objective,
                PlanningState = "Not delegated",
                PlanningDetail = "Saved during owner-authorized GeoShift onboarding; not delegated or billed.",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        return new(command is null
            ? "GeoShift is registered. No coding has started. Scoped GitHub read/write access and repository allowlists still need owner setup."
            : "GeoShift is registered, and I've saved that coding request. No coding has started. Configure scoped GitHub access and repository allowlists, then repeat the request to authorize a reviewable draft.");
    }
}


/// <summary>
/// Owner-facing, read-only development setup checklist. Never includes token
/// contents or grants authorization. Independent CI is checked by the worker
/// before any paid planning, not inferred from these local settings.
/// </summary>
public static class ProjectSetupReadiness
{
    public sealed record Snapshot(bool Ready, string[] Missing);

    private static bool InList(string variable, string repository) =>
        (Environment.GetEnvironmentVariable(variable) ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(repository, StringComparer.OrdinalIgnoreCase);

    public static Snapshot Evaluate(ManagedProject project, bool aiConfigured)
    {
        var missing = new List<string>();
        if (project.Paused)
            missing.Add("Project is paused");
        if (!string.Equals(Environment.GetEnvironmentVariable("ARCHER_PROJECT_AUTONOMY_ENABLED"),
            "true", StringComparison.OrdinalIgnoreCase))
            missing.Add("Enable project autonomy");
        if (!aiConfigured)
            missing.Add("Configure the AI API key");
        if (!string.Equals(Environment.GetEnvironmentVariable("ARCHER_CODING_ENABLED"),
            "true", StringComparison.OrdinalIgnoreCase))
            missing.Add("Enable coding");
        if (!ProjectGitHubCredentials.IsConfigured(project.Repository))
            missing.Add("Configure scoped GitHub read and write access");
        if (!InList("ARCHER_GITHUB_ALLOWLIST", project.Repository))
            missing.Add("Add repository to GitHub read allowlist");
        if (!InList("ARCHER_GITHUB_WRITE_REPOS", project.Repository))
            missing.Add("Add repository to GitHub write allowlist");
        if (!SandboxPolicy.Configured(project))
            missing.Add("Configure the required isolated sandbox");
        if (!SelfDevelopmentPolicy.Configured(project))
            missing.Add("Enable approved Archer self-development paths");
        return new(missing.Count == 0, missing.ToArray());
    }
}
