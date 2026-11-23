using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Archer.StateMerge;

static string Argument(string[] args, string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    if (i < 0) return fallback;
    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException("A file path is missing for " + name);
    return args[i + 1];
}
static (string Data, string Hash) Read(string path)
{
    var file = new FileInfo(path);
    if (!file.Exists || file.Length > 50_000_000 || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        throw new InvalidDataException("Missing, linked or oversized state file: " + path);
    var bytes = File.ReadAllBytes(path);
    return (Encoding.UTF8.GetString(bytes), Convert.ToHexString(SHA256.HashData(bytes)));
}
static bool ArcherRunning()
{
    try
    {
        using var client = new TcpClient();
        var connect = client.ConnectAsync("127.0.0.1", 5087);
        return connect.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected;
    }
    catch { return false; }
}
try
{
    bool prepare = args.Contains("--prepare", StringComparer.OrdinalIgnoreCase);
    if (args.Any(x => x.StartsWith("--", StringComparison.Ordinal) &&
        x is not ("--prepare" or "--installed" or "--visual" or "--output")))
        throw new ArgumentException("Only --prepare, --installed, --visual and --output are supported.");
    var repo = Directory.GetCurrentDirectory();
    var visual = Path.GetFullPath(Argument(args, "--visual",
        Path.Combine(repo, "Archer.MissionControl", "Data", "archer-state.json")));
    var installed = Path.GetFullPath(Argument(args, "--installed",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Archer", "Data", "archer-state.json")));
    var output = Path.GetFullPath(Argument(args, "--output",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Archer-Merge-Review")));
    if (visual == installed) throw new InvalidDataException("The sources must be two different saved files.");
    var first = Read(visual);
    var second = Read(installed);
    var plan = StateMerger.Analyze(first.Data, second.Data);
    Console.WriteLine("ARCHER HISTORY PREVIEW - no source files changed.");
    Console.WriteLine("Visual Studio conversations: " + plan.VisualMessages +
        "; Installed Archer conversations: " + plan.InstalledMessages);
    Console.WriteLine("Additional unique entries: " + plan.NewMessages +
        "; shared entries: " + plan.DuplicateMessages);
    Console.WriteLine("Available in proposed live chat: " + ((System.Text.Json.Nodes.JsonArray)plan.ProposedState["conversation"]!).Count);
    Console.WriteLine("Full combined history in separate archive: " + plan.FullConversationArchive.Count +
        "; older archive-only entries: " + plan.ArchivedOnlyMessages);
    Console.WriteLine("Visual Studio projects: " + plan.Projects + "; development batches: " + plan.Batches +
        " (preserved without modifications).");
    Console.WriteLine("Installed-only missions: " + plan.InstalledOnlyMissions +
        "; differing mission records: " + plan.ConflictingMissions +
        " (NOT automatically merged).");
    if (!prepare)
    {
        Console.WriteLine("READ ONLY. Your two originals and your Desktop backups are unchanged.");
        Console.WriteLine("Review this report before preparing a separate proposed file.");
        return 0;
    }
    if (ArcherRunning()) throw new InvalidOperationException("Stop Archer before preparing a snapshot.");
    if (Directory.Exists(output) || File.Exists(output))
        throw new IOException("Review folder already exists. Existing files were not touched.");
    if (Read(visual).Hash != first.Hash || Read(installed).Hash != second.Hash)
        throw new IOException("A source changed during review. No merge prepared.");
    Directory.CreateDirectory(output);
    // The resulting files contain private user content. This is a separate,
    // local review package; it is NOT installed and must never be uploaded.
    File.WriteAllText(Path.Combine(output, "archer-state.json"),
        StateMerger.Serialize(plan.ProposedState), new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(output, "conversation-archive.json"),
        StateMerger.Serialize(plan.FullConversationArchive), new UTF8Encoding(false));
    Console.WriteLine("Prepared a NEW local review folder: " + output);
    Console.WriteLine("NOT INSTALLED. Both originals remain unchanged. Keep this folder private.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("No data changed: " + error.Message);
    return 1;
}
