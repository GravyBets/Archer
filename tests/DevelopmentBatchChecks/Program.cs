using System.Net;
using System.Text;
using Archer.MissionControl.Domain;
using Archer.MissionControl.Services;

var now = DateTimeOffset.UtcNow;
var first = new DevelopmentTask();
var second = new DevelopmentTask();
var batch = new DevelopmentBatch { Armed = true, ExpiresAtUtc = now.AddHours(24),
    AttemptsRemaining = 4, Tasks = [first, second] };
void Check(bool value, string label)
{
    if (!value) throw new Exception(label);
    Console.WriteLine("PASS: " + label);
}
Check(!DevelopmentBatchPolicy.Claim(batch, second, now), "no task skipping");
Check(DevelopmentBatchPolicy.Claim(batch, first, now) && batch.AttemptsRemaining == 3,
    "first attempt persisted before external action");
Check(!DevelopmentBatchPolicy.Claim(batch, first, now), "duplicate claim refused");
first.Status = "Testing";
DevelopmentBatchPolicy.CompleteCi(batch, first, "Failed", "build failure");
Check(first.Status == "Repair queued" && batch.Armed, "one failed build admits repair");
Check(DevelopmentBatchPolicy.Claim(batch, first, now) && first.Attempts == 2,
    "repair consumes second attempt");
first.Status = "Testing";
DevelopmentBatchPolicy.CompleteCi(batch, first, "Verified", "build and tests");
Check(first.Status == "Verified" && batch.Armed, "next independent task can run");
Check(DevelopmentBatchPolicy.Claim(batch, second, now), "continuation");
second.Status = "Testing";
DevelopmentBatchPolicy.CompleteCi(batch, second, "Failed", "test fail");
Check(DevelopmentBatchPolicy.Claim(batch, second, now), "second repair");
second.Status = "Testing";
DevelopmentBatchPolicy.CompleteCi(batch, second, "Failed", "still failing");
Check(!batch.Armed && second.Status == "Blocked" && batch.AttemptsRemaining == 0, "repair cap");
var recovered = new DevelopmentBatch { Armed = true, ResumeAfterRestart = true,
    ExpiresAtUtc = now.AddHours(1), AttemptsRemaining = 2,
    Tasks = [new DevelopmentTask { Status = "Testing" }] };
DevelopmentBatchPolicy.Recover(recovered, now);
Check(recovered.Armed && recovered.AttemptsRemaining == 2, "safe restart preserves original allowance");
recovered.Tasks[0].Status = "Publishing";
DevelopmentBatchPolicy.Recover(recovered, now);
Check(!recovered.Armed && recovered.Tasks[0].Status == "Blocked", "uncertain publication cannot retry");
var expired = new DevelopmentBatch { Armed = true, ExpiresAtUtc = now.AddSeconds(-1),
    ResumeAfterRestart = true, AttemptsRemaining = 2, Tasks = [new DevelopmentTask()] };
Check(!DevelopmentBatchPolicy.Claim(expired, expired.Tasks[0], now), "expired permission cannot spend");
DevelopmentBatchPolicy.Recover(expired, now);
Check(!expired.Armed, "expired authorization cannot resume");
var fake = new FakeGitHub();
using var http = new HttpClient(fake);
var verifier = new ProjectCiVerifier(http);
var geo = ProjectPolicy.Register(new("GeoShift", "GravyBets/GeoShift", ".NET", ["GeoShift.Core"], "archer-ci.yml", "GeoShift build and tests", ["Build Web", "Run core regression tests"]));
var other = ProjectPolicy.Register(new("Future app", "owner/future", "TypeScript", ["src"], "quality.yml", "Quality", ["Compile", "Test"], 5, 2));
var proposal = new CodeProposal { Repository = "GravyBets/GeoShift", BaseBranch = "master",
    BranchName = "archer/draft-example", PublishedHeadSha = new string('a',40), PullRequestNumber = 7 };
Check(await verifier.ReadyAsync(geo, default), "expected workflow present");
Check((await verifier.InspectAsync(geo, proposal, default)).Status == "Verified", "exact-head successful build and tests");
fake.Mode = "failed";
Check((await verifier.InspectAsync(geo, proposal, default)).Status == "Failed", "failed build is not green");
fake.Mode = "skipped";
Check((await verifier.InspectAsync(geo, proposal, default)).Status == "Blocked", "skipped tests never verify");
fake.Mode = "pending";
Check((await verifier.InspectAsync(geo, proposal, default)).Status == "Testing", "missing run never verifies");
fake.Mode = "changed";
Check((await verifier.InspectAsync(geo, proposal, default)).Status == "Blocked", "changed PR head refused");

Check(ProjectPolicy.Eligible(geo, "GeoShift.Core/Services/KmlConverter.cs", 2400), "registered .NET source eligible");
Check(!ProjectPolicy.Eligible(other, "GeoShift.Core/Services/KmlConverter.cs", 2400), "unrelated repository paths cannot cross project scope");
Check(ProjectPolicy.Eligible(other, "src/components/Map.tsx", 1200), "registered TypeScript source eligible");
Check(!ProjectPolicy.Eligible(other, "src/tests/Map.test.tsx", 1200), "independent tests protected");
Check(!ProjectPolicy.Eligible(other, "src/../production.ts", 1000), "path traversal refused");
Check(!ProjectPolicy.Eligible(other, "src/settings.py", 1000), "sensitive configuration protected");
Check(!ProjectPolicy.Eligible(other, "src/Map.tsx", 9001), "bounded source size");
Check(ProjectGitHubCredentials.Prefix(geo.Repository) != ProjectGitHubCredentials.Prefix(other.Repository), "repository credentials isolated");
Check(ProjectGitHubCredentials.Prefix("OWNER/FUTURE") == ProjectGitHubCredentials.Prefix(other.Repository), "repository identity case insensitive");
Check((await verifier.InspectAsync(other, proposal, default)).Status == "Blocked", "cross-project PR rejected before request");

var validPlan = new ProjectPlanDraft { Summary = "Improve display", Tasks = [
    new ProjectPlanTaskDraft { Title = "Map", Description = "Improve view state", Paths = ["src/components/Map.tsx"] }
] };
ProjectPolicy.ValidatePlan(validPlan, ["src/components/Map.tsx"]);
validPlan.Tasks[0].Paths = ["GeoShift.Core/Services/KmlConverter.cs"];
try { ProjectPolicy.ValidatePlan(validPlan, ["src/components/Map.tsx"]); throw new Exception("Cross-project plan accepted"); }
catch (InvalidOperationException) { Console.WriteLine("PASS: cross-project plan refused"); }

var state = new ArcherState { Projects = [geo, other] };
var queuedGeo = new DevelopmentBatch { ProjectId = geo.Id, Armed = true, ExpiresAtUtc = now.AddHours(1), Tasks = [new DevelopmentTask()] };
var queuedOther = new DevelopmentBatch { ProjectId = other.Id, Armed = true, ExpiresAtUtc = now.AddHours(1), Tasks = [new DevelopmentTask()] };
state.DevelopmentBatches = [queuedGeo, queuedOther];
Check(ProjectPolicy.Select(state, now) == queuedOther, "higher project priority selected");
queuedGeo.Tasks[0].Status = "Testing";
Check(ProjectPolicy.Select(state, now) == queuedGeo, "observe in-flight CI first");
geo.Paused = true;
Check(ProjectPolicy.Select(state, now) == queuedOther, "paused project cannot execute");
queuedOther.Tasks[0].AttemptedAtUtc = [now.AddMinutes(-10), now.AddHours(-25)];
Check(ProjectPolicy.AttemptsToday(state, other.Id, now) == 1 && ProjectPolicy.AttemptsToday(state, geo.Id, now) == 0, "rolling project quota is independent");

var predecessor = new ProjectObjective { Description = "Finish authentication" };
var dependent = new ProjectObjective { Description = "Build dashboard", DependsOn = [predecessor.Id] };
other.Objectives = [predecessor, dependent];
Check(!ProjectPolicy.ObjectiveReady(other, dependent), "unaccepted prerequisite blocks dependent objective");
predecessor.AcceptedAtUtc = now;
Check(ProjectPolicy.ObjectiveReady(other, dependent), "owner acceptance releases dependent objective");
Check(!ProjectPolicy.ObjectiveReady(geo, dependent), "dependencies cannot be satisfied across projects");
dependent.PlanningState = "Delegated";
dependent.DelegatedUntilUtc = now.AddHours(1);
dependent.ResumeDelegationAfterRestart = true;
Check(ProjectPolicy.CanPlanDelegated(other, dependent, now), "accepted prerequisites unlock delegated planning");
Check(ProjectPolicy.ClaimDelegated(other, dependent, now), "delegated planning reserves exactly one attempt");
Check(!ProjectPolicy.ClaimDelegated(other, dependent, now), "repeated planning claim refused");
ProjectPolicy.RecoverDelegation(dependent, now);
Check(dependent.PlanningState == "Blocked" && dependent.DelegatedUntilUtc is null, "interrupted paid plan cannot automatically retry");
var delegated = new ProjectObjective { PlanningState = "Delegated", DelegatedUntilUtc = now.AddHours(1), ResumeDelegationAfterRestart = true };
ProjectPolicy.RecoverDelegation(delegated, now);
Check(delegated.PlanningState == "Delegated", "explicit restart authorization preserves unclaimed objective");
ProjectPolicy.RecoverDelegation(delegated, now.AddHours(2));
Check(delegated.PlanningState == "Not delegated" && delegated.DelegatedUntilUtc is null, "expired delegated objective disarmed");

ProjectPolicy.AddMemory(geo, "Decision", "unique geoshift context", true);
ProjectPolicy.AddMemory(other, "Architecture", "map rendering uses canvas", true);
ProjectPolicy.AddMemory(other, "Owner note", "private local note", false);
var memory = ProjectPolicy.Context(other, "map");
Check(memory.Contains("canvas") && !memory.Contains("private local note") && !memory.Contains("geoshift"), "AI memory retrieval is project-specific and opt-in");
var restored = System.Text.Json.JsonSerializer.Deserialize<ArcherState>(System.Text.Json.JsonSerializer.Serialize(state))!;
Check(restored.Projects.Single(p => p.Id == other.Id).Memory.Count == 2 &&
    restored.DevelopmentBatches.Single(b => b.ProjectId == other.Id).Tasks[0].AttemptedAtUtc.Count == 2,
    "project history, objectives and quota survive serialization");

fake.Mode = "success";
fake.Repository = "owner/future";
fake.Workflow = "quality.yml";
fake.Job = "Quality";
fake.Build = "Compile";
fake.Test = "Test";
proposal.Repository = other.Repository;
Check(await verifier.ReadyAsync(other, default), "second repository workflow recognized");
Check((await verifier.InspectAsync(other, proposal, default)).Status == "Verified", "second repository build and test profile verified");

var workspacePlan = new ProjectPlanDraft { Summary = "Related source refactor", Tasks = [
    new ProjectPlanTaskDraft { Title = "Map", Description = "Improve view state", Paths = ["src/components/Map.tsx"], NewPaths = ["src/components/MapState.ts"], ContextPaths = ["src/components/Legend.tsx"] }
] };
bool Refused(Action action)
{
    try { action(); return false; }
    catch (InvalidOperationException) { return true; }
}
var eligibleWorkspace = new[] { "src/components/Map.tsx", "src/components/Legend.tsx" };
Check(Refused(() => ProjectPolicy.ValidatePlan(workspacePlan, eligibleWorkspace, other)), "new files require separate project authorization");
other.AllowNewSourceFiles = true;
ProjectPolicy.ValidatePlan(workspacePlan, eligibleWorkspace, other);
Check(Refused(() => ProjectPolicy.ValidatePlan(workspacePlan, eligibleWorkspace, other, ["src/components/mapstate.ts"])), "case-insensitive new path collision refused");
workspacePlan.Tasks[0].ContextPaths = ["src/components/Map.tsx"];
Check(Refused(() => ProjectPolicy.ValidatePlan(workspacePlan, eligibleWorkspace, other)), "editable and read-only paths cannot overlap");
workspacePlan.Tasks[0].ContextPaths = ["src/components/Legend.tsx"];
workspacePlan.Tasks.Add(new ProjectPlanTaskDraft { Title = "Legend", Description = "Modify legend", Paths = ["src/components/Legend.tsx"] });
Check(Refused(() => ProjectPolicy.ValidatePlan(workspacePlan, eligibleWorkspace, other)), "independent tasks cannot depend on another task's edits");
workspacePlan.Tasks.RemoveAt(1);
var prefix = ProjectGitHubCredentials.Prefix(other.Repository);
Environment.SetEnvironmentVariable(prefix + "_READ_TOKEN", "synthetic-reader");
Environment.SetEnvironmentVariable(prefix + "_WRITE_TOKEN", "synthetic-writer");
Environment.SetEnvironmentVariable("ARCHER_CODING_ENABLED", "true");
Environment.SetEnvironmentVariable("ARCHER_GITHUB_WRITE_REPOS", other.Repository);
var fakePublisher = new FakeWorkspaceGitHub(other.Repository);
using var publisherHttp = new HttpClient(fakePublisher);
var publisher = new GitHubDraftPrPublisher(publisherHttp);
var workspace = new CodeProposal { Repository = other.Repository, BaseBranch = "master",
    BaseTreeSha = new string('b', 40), WorkspaceDraft = true, Title = "Map workspace",
    Files = [
        new CodeProposalFile { Path = "src/components/Map.tsx", ExpectedBlobSha = new string('c',40), ProposedContent = "export const Map = 1;" },
        new CodeProposalFile { Path = "src/components/MapState.ts", IsNew = true, ProposedContent = "export const state = 1;" }
    ] };
var resultWorkspace = await publisher.PublishWorkspaceAsync(other, workspace, default);
Check(resultWorkspace.Url == "https://github.com/owner/future/pull/9" &&
    workspace.PublishedHeadSha == new string('e',40), "atomic workspace publication records exact commit");
Check(fakePublisher.Writes.SequenceEqual(new[] { "/git/trees", "/git/commits", "/git/refs", "/pulls" }), "branch exists only after all edits represented in a single commit");
fakePublisher.Writes.Clear();
workspace.BaseTreeSha = new string('f',40);
try { await publisher.PublishWorkspaceAsync(other, workspace, default); throw new Exception("Stale tree accepted"); }
catch (InvalidOperationException) { Check(fakePublisher.Writes.Count == 0, "stale source tree never writes"); }
workspace.BaseTreeSha = new string('b',40);
workspace.Files[1].Path = "src/components/map.tsx";
try { await publisher.PublishWorkspaceAsync(other, workspace, default); throw new Exception("Case collision accepted"); }
catch (InvalidOperationException) { Check(fakePublisher.Writes.Count == 0, "case collision never writes"); }

other.CumulativeWorkspaces = true;
workspacePlan.Tasks.Add(new ProjectPlanTaskDraft { Title = "Integrate", Description = "Use earlier state", Paths = ["src/components/MapState.ts"], ContextPaths = ["src/components/Map.tsx"] });
ProjectPolicy.ValidatePlan(workspacePlan, eligibleWorkspace, other);
Check(true, "ordered cumulative plan can consume earlier new source");
workspacePlan.Tasks.Reverse();
Check(Refused(() => ProjectPolicy.ValidatePlan(workspacePlan, eligibleWorkspace, other)), "future files are not eligible before creation");
workspacePlan.Tasks.Reverse();

fakePublisher.Writes.Clear();
fakePublisher.CumulativeBranch = resultWorkspace.Branch;
workspace.Files[1].Path = "src/components/MapState.ts";
workspace.WorkspaceBranch = resultWorkspace.Branch;
workspace.WorkspaceParentSha = new string('a', 40);
workspace.WorkspacePullRequestNumber = 9;
var nextWorkspace = await publisher.PublishWorkspaceAsync(other, workspace, default);
Check(nextWorkspace.Url == resultWorkspace.Url && nextWorkspace.Branch == resultWorkspace.Branch,
    "continuation retains the same draft PR");
Check(fakePublisher.Writes.SequenceEqual(new[] { "/git/trees", "/git/commits", "/git/refs/heads/" + resultWorkspace.Branch }),
    "continuation commits once, fast-forwards once, never creates another PR");
fakePublisher.Writes.Clear();
fakePublisher.ExternalChange = true;
try { await publisher.PublishWorkspaceAsync(other, workspace, default); throw new Exception("External head change accepted"); }
catch (InvalidOperationException) { Check(fakePublisher.Writes.Count == 0, "external head changes prevent all writes"); }
fakePublisher.ExternalChange = false;
fakePublisher.DraftOpen = false;
try { await publisher.PublishWorkspaceAsync(other, workspace, default); throw new Exception("Ready-for-review PR accepted"); }
catch (InvalidOperationException) { Check(fakePublisher.Writes.Count == 0, "no writes after PR leaves draft state"); }
other.CumulativeWorkspaces = false;
try { await publisher.PublishWorkspaceAsync(other, workspace, default); throw new Exception("Unapproved continuation accepted"); }
catch (InvalidOperationException) { Check(fakePublisher.Writes.Count == 0, "continuation requires project opt-in"); }

other.CumulativeWorkspaces = true;
other.RegressionTestRoots = ["tests/unit"];
Check(ProjectPolicy.NewRegressionAllowed(other, "tests/unit/archer-map.test.ts"), "explicit additive regression path permitted");
Check(!ProjectPolicy.NewRegressionAllowed(other, "tests/unit/map.test.ts"), "existing conventional tests not editable");
Check(!ProjectPolicy.NewRegressionAllowed(other, "tests/unit/../archer-map.test.ts"), "regression traversal refused");
var regressionPlan = new ProjectPlanDraft { Summary = "Map regression", Tasks = [
    new ProjectPlanTaskDraft { Title = "Fix map", Description = "Preserve selected state", Paths = ["src/components/Map.tsx"], NewPaths = ["tests/unit/archer-map.test.ts"] }
] };
ProjectPolicy.ValidatePlan(regressionPlan, eligibleWorkspace, other);
Check(Refused(() => ProjectPolicy.ValidatePlan(regressionPlan, eligibleWorkspace, other, ["tests/unit/archer-map.test.ts"])), "existing regression path never overwritten");
regressionPlan.Tasks.Add(new ProjectPlanTaskDraft { Title = "Change test", Description = "Modify previous generated regression", Paths = ["tests/unit/archer-map.test.ts"] });
Check(Refused(() => ProjectPolicy.ValidatePlan(regressionPlan, eligibleWorkspace, other)), "later cumulative task cannot rewrite authored regression");
regressionPlan.Tasks.RemoveAt(1);
regressionPlan.Tasks[0].Paths.Clear();
Check(Refused(() => ProjectPolicy.ValidatePlan(regressionPlan, eligibleWorkspace, other)), "test-only authoring refused");
other.CumulativeWorkspaces = false;
Check(!ProjectPolicy.NewRegressionAllowed(other, "tests/unit/archer-map.test.ts"), "test authoring requires cumulative mode");

sealed class FakeGitHub : HttpMessageHandler
{
    public string Mode = "success";
    public string Repository = "GravyBets/GeoShift";
    public string Workflow = "archer-ci.yml";
    public string Job = "GeoShift build and tests";
    public string Build = "Build Web";
    public string Test = "Run core regression tests";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var sha = new string(Mode == "changed" ? 'b' : 'a',40);
        var path = request.RequestUri!.AbsolutePath;
        if (!path.StartsWith("/repos/" + Repository + "/")) throw new Exception("Cross-project request: " + path);
        object payload;
        if (path.EndsWith("/pulls/7"))
            payload = new { state = "open", head = new { sha, @ref = "archer/draft-example",
                repo = new { full_name = Repository } }, @base = new { @ref = "master" } };
        else if (path.EndsWith("/" + Workflow))
            payload = new { state = "active", path = ".github/workflows/" + Workflow };
        else if (path.EndsWith("/" + Workflow + "/runs"))
            payload = new { workflow_runs = Mode == "pending" ? Array.Empty<object>() :
                new object[] { new { id = 10, head_sha = sha, head_branch = "archer/draft-example",
                    @event = "pull_request", status = "completed",
                    conclusion = Mode == "failed" ? "failure" : "success" } } };
        else if (path.EndsWith("/runs/10/jobs"))
            payload = new { jobs = new[] { new { name = Job,
                conclusion = Mode == "failed" ? "failure" : "success", steps = new[] {
                    new { name = Build, conclusion = Mode == "failed" ? "failure" : "success" },
                    new { name = Test, conclusion = Mode is "skipped" or "failed" ? "skipped" : "success" }
                } } } };
        else throw new Exception("Unexpected CI request: " + path);
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}

sealed class FakeWorkspaceGitHub(string repository) : HttpMessageHandler
{
    public List<string> Writes { get; } = [];
    public string? CumulativeBranch;
    public bool ExternalChange;
    public bool DraftOpen = true;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Authorization?.Parameter != "synthetic-writer")
            throw new Exception("Wrong scoped credential");
        var prefix = "/repos/" + repository;
        var absolute = request.RequestUri!.AbsolutePath;
        if (!absolute.StartsWith(prefix + "/", StringComparison.Ordinal)) throw new Exception("Wrong workspace repository");
        var path = absolute[prefix.Length..];
        object payload;
        string h(char c) => new string(c, 40);
        if (request.Method == HttpMethod.Get)
        {
            if (path == "/git/ref/heads/master" || path == "/git/ref/heads/" + CumulativeBranch) payload = new { @object = new { sha = h('a') } };
            else if (path == "/pulls/9") payload = new { state = "open", draft = DraftOpen, head = new { @ref = CumulativeBranch, sha = ExternalChange ? h('f') : h('a'), repo = new { full_name = repository } }, @base = new { @ref = "master" } };
            else if (path == "/git/commits/" + h('a')) payload = new { tree = new { sha = h('b') } };
            else if (path == "/git/trees/" + h('b')) payload = new { truncated = false, tree = new[] {
                new { path = "src", mode = "040000", type = "tree", sha = h('f') },
                new { path = "src/components", mode = "040000", type = "tree", sha = h('f') },
                new { path = "src/components/Map.tsx", mode = "100644", type = "blob", sha = h('c') }
            } };
            else throw new Exception("Unexpected read " + path);
        }
        else
        {
            if (request.Method != HttpMethod.Post && !(request.Method == HttpMethod.Patch && path == "/git/refs/heads/" + CumulativeBranch)) throw new Exception("Unsafe write " + request.Method);
            Writes.Add(path);
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            if (path == "/git/trees")
            {
                if (root.GetProperty("base_tree").GetString() != h('b') || root.GetProperty("tree").GetArrayLength() != 2)
                    throw new Exception("Tree not atomic");
                payload = new { sha = h('d') };
            }
            else if (path == "/git/commits")
            {
                if (root.GetProperty("tree").GetString() != h('d') || root.GetProperty("parents")[0].GetString() != h('a'))
                    throw new Exception("Wrong commit ancestry");
                payload = new { sha = h('e') };
            }
            else if (path == "/git/refs")
            {
                if (!(root.GetProperty("ref").GetString() ?? "").StartsWith("refs/heads/archer/draft-") ||
                    root.GetProperty("sha").GetString() != h('e')) throw new Exception("Unsafe branch");
                payload = new { };
            }
            else if (path == "/git/refs/heads/" + CumulativeBranch)
            {
                if (root.GetProperty("force").GetBoolean() || root.GetProperty("sha").GetString() != h('e')) throw new Exception("Not a safe fast-forward");
                payload = new { @object = new { sha = h('e') } };
            }
            else if (path == "/pulls")
            {
                if (!root.GetProperty("draft").GetBoolean() || root.GetProperty("base").GetString() != "master")
                    throw new Exception("Not a draft");
                payload = new { number = 9, html_url = "https://github.com/" + repository + "/pull/9", head = new { sha = h('e') } };
            }
            else throw new Exception("Unexpected write " + path);
        }
        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
    }
}
