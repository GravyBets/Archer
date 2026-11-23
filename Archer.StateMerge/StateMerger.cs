using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Archer.StateMerge;

/// <summary>
/// Merge only history that has stable, content-verifiable message identities.
/// Visual Studio remains the authority for all other state and permissions.
/// </summary>
public static class StateMerger
{
    public sealed record Outcome(JsonObject ProposedState, JsonArray FullConversationArchive,
        int VisualMessages, int InstalledMessages, int NewMessages, int DuplicateMessages,
        int ArchivedOnlyMessages, int VisualMissions, int InstalledOnlyMissions,
        int ConflictingMissions, int Projects, int Batches);

    private sealed record Entry(Guid Id, DateTimeOffset Time, JsonNode Node);

    public static Outcome Analyze(string visualJson, string installedJson)
    {
        var visual = ParseState(visualJson, "Visual Studio", requireDevelopmentMetadata: true);
        var installed = ParseState(installedJson, "Installed Archer", requireDevelopmentMetadata: false);
        var visualMessages = ReadConversation(visual, "Visual Studio");
        var installedMessages = ReadConversation(installed, "Installed Archer");
        var merged = new Dictionary<Guid, Entry>();
        foreach (var e in visualMessages) merged.Add(e.Id, e);

        var added = 0;
        var duplicates = 0;
        foreach (var e in installedMessages)
        {
            if (merged.TryGetValue(e.Id, out var existing))
            {
                if (!JsonNode.DeepEquals(existing.Node, e.Node))
                    throw new InvalidDataException("Conflicting conversation identifiers. No merge is safe.");
                duplicates++;
            }
            else { merged.Add(e.Id, e); added++; }
        }
        var all = merged.Values.OrderBy(e => e.Time).ThenBy(e => e.Id).ToArray();
        var live = all.TakeLast(100).ToArray();
        var fullArchive = new JsonArray();
        var liveConversation = new JsonArray();
        foreach (var e in all) fullArchive.Add(e.Node.DeepClone());
        foreach (var e in live) liveConversation.Add(e.Node.DeepClone());
        var proposed = (JsonObject)visual.DeepClone();
        proposed["conversation"] = liveConversation;

        // Other state is deliberately NOT automatically merged. Report
        // differing mission IDs/values without printing sensitive content.
        var visualMissions = ReadMissionIndex(visual, "Visual Studio");
        var installedMissions = ReadMissionIndex(installed, "Installed Archer");
        var installedOnly = installedMissions.Keys.Count(id => !visualMissions.ContainsKey(id));
        var conflicting = installedMissions.Count(kv => visualMissions.TryGetValue(kv.Key, out var item) &&
            !JsonNode.DeepEquals(item, kv.Value));
        return new(proposed, fullArchive, visualMessages.Count, installedMessages.Count,
            added, duplicates, all.Length - live.Length, visualMissions.Count,
            installedOnly, conflicting, ((JsonArray)visual["projects"]!).Count,
            ((JsonArray)visual["developmentBatches"]!).Count);
    }

    private static JsonObject ParseState(string json, string label, bool requireDevelopmentMetadata)
    {
        if (json.Length > 100_000_000) throw new InvalidDataException("State is too large to review safely.");
        var root = JsonNode.Parse(json) as JsonObject ??
            throw new InvalidDataException(label + " state must be an object.");

        // Every version needs real mission and conversation arrays. Never
        // interpret a missing or malformed conversation as empty history.
        foreach (var key in new[] { "missions", "conversation" })
            if (root[key] is not JsonArray)
                throw new InvalidDataException(label + " has a missing or invalid " + key + " array.");

        // Pre-project installed Archer versions never serialized these newer
        // properties. They are optional ONLY on the older, secondary source.
        // A present field of the wrong type is still a hard error, and the
        // authoritative Visual Studio copy must retain both valid arrays.
        foreach (var key in new[] { "projects", "developmentBatches" })
            if ((requireDevelopmentMetadata || root.ContainsKey(key)) && root[key] is not JsonArray)
                throw new InvalidDataException(label + " has a missing or invalid " + key + " array.");

        return root;
    }

    private static List<Entry> ReadConversation(JsonObject state, string label)
    {
        var list = new List<Entry>();
        var seen = new HashSet<Guid>();
        foreach (var item in (JsonArray)state["conversation"]!)
        {
            if (item is not JsonObject obj ||
                !Guid.TryParse(obj["id"]?.GetValue<string>(), out var id) || id == Guid.Empty ||
                obj["role"] is not JsonValue role || !role.TryGetValue<string>(out _) ||
                obj["content"] is not JsonValue content || !content.TryGetValue<string>(out _) ||
                !DateTimeOffset.TryParse(obj["createdAtUtc"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var when))
                throw new InvalidDataException(label + " has a malformed conversation entry.");
            if (!seen.Add(id)) throw new InvalidDataException(label + " contains repeated conversation IDs.");
            list.Add(new Entry(id, when, obj));
            if (list.Count > 10000) throw new InvalidDataException("Too many messages for automated review.");
        }
        return list;
    }

    private static Dictionary<Guid, JsonNode> ReadMissionIndex(JsonObject state, string label)
    {
        var result = new Dictionary<Guid, JsonNode>();
        foreach (var item in (JsonArray)state["missions"]!)
        {
            if (item is not JsonObject obj ||
                !Guid.TryParse(obj["id"]?.GetValue<string>(), out var id) || id == Guid.Empty ||
                !result.TryAdd(id, obj))
                throw new InvalidDataException(label + " has malformed or duplicate mission IDs.");
        }
        return result;
    }

    public static string Serialize(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
