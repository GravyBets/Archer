using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Only a fixed, read-only mission-status command. It writes the *actual*
/// grounded exchange into the already owner-local chat journal so later AI
/// follow-ups can use it. Does NOT call a model, propose/execute tasks, grant
/// permissions or accept fabricated assistant text from browser requests.
/// </summary>
internal static class ArcherVoiceMissionJournal
{
    internal sealed record MissionView(
        string Title, string Status, int TasksCompleted, int TasksTotal);
    internal sealed record StatusResult(
        string Status, int Count, IReadOnlyList<MissionView> Missions,
        string Reply);

    internal static StatusResult RecordStatus(ArcherState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var missions = state.Missions.Take(8)
            .Select(m => new MissionView(
                m.Title[..Math.Min(m.Title.Length, 100)],
                m.Status[..Math.Min(m.Status.Length, 30)],
                m.Tasks.Count(t => t.Status == "Completed"),
                m.Tasks.Count))
            .ToArray();

        var response = missions.Length == 0
            ? "You don't have any missions yet."
            : state.Missions.Count == 1
                ? "You've got one mission: " +
                  missions[0].Title[..Math.Min(missions[0].Title.Length, 80)] +
                  ". Its status is " + missions[0].Status + "."
                : "You've got " + state.Missions.Count +
                  " missions. First listed: " +
                  missions[0].Title[..Math.Min(missions[0].Title.Length, 80)] +
                  ", status " + missions[0].Status + ".";

        state.Conversation.Add(new ConversationEntry
        {
            Role = "user",
            Content = "List my missions."
        });
        state.Conversation.Add(new ConversationEntry
        {
            Role = "assistant",
            Content = response
        });
        if (state.Conversation.Count > 100)
            state.Conversation.RemoveRange(0, state.Conversation.Count - 100);
        return new StatusResult("ok", state.Missions.Count, missions, response);
    }
}
