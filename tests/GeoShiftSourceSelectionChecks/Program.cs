using Archer.MissionControl.Services;
using File = Archer.MissionControl.Services.GitHubRepositoryReader.ReviewableFile;

var files = new File[]
{
    new("GeoShift.Core/KmlCsvConverter.cs", "sha", 3000),
    new("GeoShift.Core/KmzAnalyzer.cs", "sha", 1000),
    new("GeoShift.Tests/KmlCsvTests.cs", "sha", 1000),
    new("GeoShift.Web/Program.cs", "sha", 1000),
    new("GeoShift.Core/KmlCsvHuge.cs", "sha", 9001),
    new("Other/KmlCsvConverter.cs", "sha", 1000)
};
var selected = GeoShiftSourceSelection.Select(files, "Fix KML CSV converter quoting for geographic coordinates.");
if (!selected.SequenceEqual(new[] { "GeoShift.Core/KmlCsvConverter.cs" }))
    throw new Exception("Incorrect selection or unsafe scope.");
void Reject(IEnumerable<File> candidates, string task)
{
    try { GeoShiftSourceSelection.Select(candidates, task); }
    catch (ArgumentException) { return; }
    throw new Exception("Expected selection to fail closed.");
}
Reject(files, "Make the entire application better.");
Reject(files.Concat(new[]
{
    new File("GeoShift.Core/KmlCsvOther.cs", "sha", 1000),
    new File("GeoShift.Core/KmlCsvFormat.cs", "sha", 1000)
}), "Fix KML CSV data.");
Console.WriteLine("PASS: deterministic GeoShift selection, scope and ambiguity gates.");


// Distinct commit and tree identities reproduce the live Archer pilot failure.
// This transport never calls GitHub or uses a real credential.
var oldAllowlist = Environment.GetEnvironmentVariable("ARCHER_GITHUB_ALLOWLIST");
try
{
    Environment.SetEnvironmentVariable("ARCHER_GITHUB_ALLOWLIST", "owner/example");
    var transport = new SnapshotTransport();
    using var http = new HttpClient(transport);
    var reader = new GitHubRepositoryReader(http);
    const string repositoryUrl = "https://github.com/owner/example";
    var inventory = await reader.ListReviewableFilesAsync(repositoryUrl, default, fresh: true);
    if (inventory.SnapshotCommitSha != SnapshotTransport.Commit || inventory.TreeSha != SnapshotTransport.Tree)
        throw new Exception("Commit and tree identities must stay distinct.");
    var preview = await reader.PreviewFilesAsync(repositoryUrl, ["src/Example.js"], default, inventory);
    if (preview.TreeSha != SnapshotTransport.Tree || preview.Files.Single().Content != "const x = 1;" ||
        !preview.Files.Single().Url.Contains("/blob/" + SnapshotTransport.Commit + "/"))
        throw new Exception("Source preview must be pinned to the resolved commit and tree.");
    var count = transport.Requests.Count;
    await reader.ListReviewableFilesAsync(repositoryUrl, default);
    if (transport.Requests.Count != count) throw new Exception("Default snapshot should remain cached.");
    var historical = await reader.ListReviewableFilesAsync(repositoryUrl, default, commitSha: SnapshotTransport.OlderCommit);
    if (historical.TreeSha != SnapshotTransport.OlderTree || historical.SnapshotCommitSha != SnapshotTransport.OlderCommit)
        throw new Exception("Cumulative workspace must resolve the explicit commit to its own tree.");
    var cached = await reader.ListReviewableFilesAsync(repositoryUrl, default);
    if (cached.SnapshotCommitSha != SnapshotTransport.Commit) throw new Exception("Historical read polluted default cache.");
    if (transport.Requests.Any(p => p.Contains("/git/trees/main") || p.Contains("/git/trees/" + SnapshotTransport.Commit) ||
        p.Contains("/git/trees/" + SnapshotTransport.OlderCommit)))
        throw new Exception("Reader queried a tree using a branch or commit SHA.");
    transport.Mismatch = true;
    try
    {
        await reader.ListReviewableFilesAsync(repositoryUrl, default, fresh: true);
        throw new Exception("Mismatched tree was accepted.");
    }
    catch (InvalidOperationException) { }
    Console.WriteLine("PASS: immutable commit/tree resolution, pinned previews, cache isolation and mismatch refusal.");
}
finally { Environment.SetEnvironmentVariable("ARCHER_GITHUB_ALLOWLIST", oldAllowlist); }

sealed class SnapshotTransport : HttpMessageHandler
{
    public static readonly string Commit = new('a', 40), Tree = new('b', 40), Blob = new('c', 40),
        OlderCommit = new('d', 40), OlderTree = new('e', 40);
    public List<string> Requests { get; } = [];
    public bool Mismatch;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get) throw new Exception("Unexpected write");
        var path = request.RequestUri!.AbsolutePath["/repos/owner/example".Length..];
        Requests.Add(path);
        object response;
        if (path == "") response = new { default_branch = "main" };
        else if (path == "/git/ref/heads/main") response = new { @object = new { type = "commit", sha = Commit } };
        else if (path == "/git/commits/" + Commit) response = new { sha = Commit, tree = new { sha = Tree } };
        else if (path == "/git/commits/" + OlderCommit) response = new { sha = OlderCommit, tree = new { sha = OlderTree } };
        else if (path == "/git/trees/" + Tree || path == "/git/trees/" + OlderTree)
            response = new { sha = Mismatch ? Commit : path.EndsWith(Tree) ? Tree : OlderTree, truncated = false,
                tree = new[] { new { path = "src/Example.js", mode = "100644", type = "blob", sha = Blob, size = 12 } } };
        else if (path == "/git/blobs/" + Blob)
            response = new { encoding = "base64", content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("const x = 1;")) };
        else throw new Exception("Unexpected snapshot request: " + path);
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json") });
    }
}
