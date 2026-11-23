using System.Text.Json.Nodes;
using Archer.StateMerge;

void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
JsonObject Message(Guid id, string content, int seconds) => new()
{
    ["id"] = id.ToString(), ["role"] = "user", ["content"] = content,
    ["createdAtUtc"] = DateTimeOffset.Parse("2026-11-20T12:00:00Z").AddSeconds(seconds).ToString("O"),
    ["customFlag"] = "keep"
};
JsonObject State(IEnumerable<JsonObject> messages, JsonArray? missions = null) => new()
{
    ["missions"] = missions?.DeepClone() ?? new JsonArray(),
    ["conversation"] = new JsonArray(messages.Select(m => (JsonNode?)m.DeepClone()).ToArray()),
    ["projects"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["settings"] = "private" }),
    ["developmentBatches"] = new JsonArray(new JsonObject { ["armed"] = false, ["attemptsRemaining"] = 1 }),
    ["unknownFutureFeature"] = new JsonObject { ["enabled"] = true }
};
var first = Enumerable.Range(0, 26).Select(i => Message(Guid.NewGuid(), "secret dev " + i, 300 + i)).ToArray();
var second = Enumerable.Range(0, 78).Select(i => Message(Guid.NewGuid(), "secret installed " + i, i)).ToArray();
var visual = State(first);
var installed = State(second);
var original = visual.ToJsonString();
var installedOriginal = installed.ToJsonString();
var result = StateMerger.Analyze(original, installedOriginal);
Check(result.VisualMessages == 26 && result.InstalledMessages == 78 &&
    result.NewMessages == 78 && result.DuplicateMessages == 0, "merges two independent histories");
Check(result.FullConversationArchive.Count == 104 && result.ArchivedOnlyMessages == 4 &&
    ((JsonArray)result.ProposedState["conversation"]!).Count == 100, "preserves entire history in archive and caps live context");
Check(result.ProposedState["projects"]!.ToJsonString() == visual["projects"]!.ToJsonString() &&
      result.ProposedState["developmentBatches"]!.ToJsonString() == visual["developmentBatches"]!.ToJsonString() &&
      result.ProposedState["unknownFutureFeature"]!.ToJsonString() == visual["unknownFutureFeature"]!.ToJsonString(),
    "preserves authoritative project permissions, coding state and future fields");
Check(visual.ToJsonString() == original && installed.ToJsonString() == installedOriginal, "never mutates input snapshots");

var legacyInstalled = (JsonObject)installed.DeepClone();
legacyInstalled.Remove("projects");
legacyInstalled.Remove("developmentBatches");
var legacyResult = StateMerger.Analyze(original, legacyInstalled.ToJsonString());
Check(legacyResult.InstalledMessages == 78 && legacyResult.NewMessages == 78 &&
    legacyResult.FullConversationArchive.Count == 104,
    "older installed Archer without project/batch fields retains all conversation history");
Check(legacyResult.ProposedState["projects"]!.ToJsonString() == visual["projects"]!.ToJsonString() &&
    legacyResult.ProposedState["developmentBatches"]!.ToJsonString() == visual["developmentBatches"]!.ToJsonString(),
    "legacy secondary data cannot overwrite authoritative development records");
var malformedLegacy = (JsonObject)legacyInstalled.DeepClone();
malformedLegacy["projects"] = "not-an-array";
try
{
    StateMerger.Analyze(original, malformedLegacy.ToJsonString());
    throw new Exception("Malformed legacy project field was accepted");
}
catch (InvalidDataException) { Check(true, "rejects present malformed legacy project metadata"); }
var missingVisualMetadata = (JsonObject)visual.DeepClone();
missingVisualMetadata.Remove("developmentBatches");
try
{
    StateMerger.Analyze(missingVisualMetadata.ToJsonString(), legacyInstalled.ToJsonString());
    throw new Exception("Missing primary development records were accepted");
}
catch (InvalidDataException) { Check(true, "requires Visual Studio's authoritative project and batch arrays"); }
var missingLegacyChat = (JsonObject)legacyInstalled.DeepClone();
missingLegacyChat.Remove("conversation");
try
{
    StateMerger.Analyze(original, missingLegacyChat.ToJsonString());
    throw new Exception("Missing legacy conversations were treated as empty");
}
catch (InvalidDataException) { Check(true, "refuses missing conversation history instead of silently losing it"); }

var archive = (JsonArray)result.FullConversationArchive;
Check(archive[0]!["content"]!.GetValue<string>() == "secret installed 0" &&
      archive.Last()!["content"]!.GetValue<string>() == "secret dev 25", "restores chronological order");
var overlapping = State(second.Take(10).Concat(first.Take(3)));
var dedup = StateMerger.Analyze(original, overlapping.ToJsonString());
Check(dedup.DuplicateMessages == 3 && dedup.NewMessages == 10 && dedup.FullConversationArchive.Count == 36,
    "deduplicates stable conversation IDs, never by matching text");
var conflicting = State(new[] { Message(Guid.Parse(first[0]["id"]!.GetValue<string>()), "altered message", 301) });
try
{
    StateMerger.Analyze(original, conflicting.ToJsonString());
    throw new Exception("Conflict was accepted");
}
catch (InvalidDataException) { Check(true, "refuses differing content under the same message ID"); }
var invalid = State(new [] { Message(Guid.NewGuid(), "private", 1) });
((JsonArray)invalid["conversation"]!)[0]!.AsObject().Remove("createdAtUtc");
try
{
    StateMerger.Analyze(original, invalid.ToJsonString());
    throw new Exception("Invalid timestamp was accepted");
}
catch (InvalidDataException) { Check(true, "refuses malformed conversation data"); }
var mission = Guid.NewGuid();
var primaryMissions = new JsonArray(new JsonObject { ["id"] = mission.ToString(), ["status"] = "Ready" });
var installedMissions = new JsonArray(new JsonObject { ["id"] = mission.ToString(), ["status"] = "Closed" },
    new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["status"] = "Running" });
var different = StateMerger.Analyze(State(first, primaryMissions).ToJsonString(),
    State(second, installedMissions).ToJsonString());
Check(different.InstalledOnlyMissions == 1 && different.ConflictingMissions == 1 &&
    different.ProposedState["missions"]!.ToJsonString() == primaryMissions.ToJsonString(),
    "does not merge conflicting mission activity or restart paid work");
Console.WriteLine("PASS: no owner data accessed, no credentials, no source file modified.");
