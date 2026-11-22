using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Direct owner-origin speech or typed intent only. Creates one bounded
/// research-only task for the existing research worker, never writes code,
/// deploys, purchases or contacts anyone. Not an AI tool-call permission.
/// </summary>
public static class ConversationResearch
{
    private static readonly Regex Directive = new(
        @"^(?:(?:hey\s+)?archer[,!:]?\s+)?(?:(?:please|i\s+want\s+you\s+to|go\s+ahead\s+and)\s+)*(?:research|investigate|look\s+into|analy[sz]e)\s+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));
    public sealed record Result(string Answer, Guid? MissionId = null, bool Accepted = false);

    public static bool LooksLikeResearch(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Directive.IsMatch(text.Trim());

    public static Result Queue(ArcherState state, string input, DateTimeOffset now)
    {
        var cleaned = input.Trim();
        var prefix = Directive.Match(cleaned);
        if (!prefix.Success)
            return new("Tell me what you want researched, and I'll prepare a report.");
        var requested = cleaned[prefix.Length..].Trim();
        if (requested.Length is < 15 or > 1000 || requested.Any(char.IsControl))
            return new("I need one specific research question in 15 to 1,000 characters.");
        if (state.Missions.Count >= 200)
            return new("The mission history is at capacity. I haven't started a new paid task.");

        var duplicate = state.Missions.FirstOrDefault(m =>
            m.CreatedAtUtc >= now.AddHours(-24) &&
            m.Summary == "Owner-authorized conversational research" &&
            string.Equals(m.Objective, requested, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            return new("That research assignment is already recorded: " + duplicate.Status +
                ". I haven't queued a duplicate.", duplicate.Id);

        var recent = state.Missions.Count(m => m.CreatedAtUtc >= now.AddHours(-24) &&
            m.Summary == "Owner-authorized conversational research");
        if (recent >= 4)
            return new("You've reached the four conversational research assignments authorized per 24 hours. No new paid task was started.");

        var mission = new Mission
        {
            Title = "Research: " + requested[..Math.Min(85, requested.Length)],
            Objective = requested,
            Summary = "Owner-authorized conversational research",
            Status = "Ready",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            AutoRunEnabled = true,
            AutoRunTasksRemaining = 1,
            Tasks =
            [
                new MissionTask
                {
                    Title = "Research and summarize",
                    Kind = "research",
                    Status = "Pending",
                    Description = "Research this specific owner request and deliver source-grounded findings, uncertainties and a brief practical next step: " +
                        requested + ". Do not perform purchases, outreach, code edits or deployment."
                }
            ]
        };
        state.Missions.Insert(0, mission);
        state.Activity.Insert(0, new ActivityEntry
        {
            MissionId = mission.Id,
            Message = "Owner delegated one bounded research task from conversation."
        });
        if (state.Activity.Count > 250) state.Activity.RemoveRange(250, state.Activity.Count - 250);
        return new("I'll research that and save the findings in Mission Control. I'll only gather information—no purchases, messages, code changes or deployment.", mission.Id, true);
    }

    public static IEnumerable<string> Status(ArcherState state) =>
        state.Missions
            .Where(m => m.Summary == "Owner-authorized conversational research")
            .Take(3)
            .Select(m => "Research: " + m.Status.ToLowerInvariant() + " — " + m.Objective);
}
