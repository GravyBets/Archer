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
fake.Draft = false;
Check((await verifier.InspectAsync(geo, proposal, default, requireDraft: true)).Status == "Blocked", "reassessment cannot continue a draft marked ready for review");
fake.Draft = true;
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
workspace.Files = [new CodeProposalFile { Path = "src/components/MapState.ts", ExpectedBlobSha = new string('2',40), ProposedContent = "export const state = 2;" }];
workspace.BaseTreeSha = new string('d',40);
workspace.WorkspaceBranch = resultWorkspace.Branch;
workspace.WorkspaceParentSha = new string('e', 40);
workspace.WorkspacePullRequestNumber = 9;
var nextWorkspace = await publisher.PublishWorkspaceAsync(other, workspace, default);
Check(nextWorkspace.Url == resultWorkspace.Url && nextWorkspace.Branch == resultWorkspace.Branch && workspace.PublishedHeadSha == new string('4',40),
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

var ranked = ProjectRepositoryContext.Rank(new GitHubRepositoryReader.ReviewableFile[]
{
    new("src/components/MapState.ts", new string('a',40), 2000),
    new("src/components/Legend.tsx", new string('b',40), 3000),
    new("src/utils/DateFormatting.ts", new string('c',40), 1000)
}, "Fix MapState persistence");
Check(ranked.SequenceEqual(new[] { "src/components/MapState.ts" }), "planner retrieves relevant component by exact/camel-case symbols");
var excerpts = ProjectRepositoryContext.Excerpts(new GitHubRepositoryReader.SourcePreviewFile[]
{
    new("src/components/MapState.ts", "", "export class MapState {}\n" + new string('a', 8000), 8025, false),
    new("src/private.ts", "", "truncated source", 10000, true)
});
Check(excerpts.Contains("export class MapState") && excerpts.Contains("[excerpt truncated]") &&
    !excerpts.Contains("src/private.ts") && excerpts.Length < 3000, "source context is bounded, actual and skips truncated blobs");

var sandboxImage = "sha256:" + new string('a',64);
var sandboxName = "archer-verify-" + new string('a',32);
var sandboxArgs = SandboxPolicy.CreateArguments(sandboxImage, sandboxName);
Check(sandboxArgs.Contains("--network=none") && sandboxArgs.Contains("--pull=never") &&
    sandboxArgs.Contains("--cap-drop=ALL") && sandboxArgs.Contains("--read-only") &&
    sandboxArgs.Contains("--user=65532:65532") && sandboxArgs.Contains("--timeout=480") &&
    sandboxArgs.Contains("--env-host=false") && !sandboxArgs.Any(a => a.StartsWith("--volume") || a.StartsWith("--mount")),
    "sandbox launch has fixed resource, identity and network limits and no host binds");
try { SandboxPolicy.CreateArguments("latest", sandboxName); throw new Exception("Mutable image accepted"); }
catch (ArgumentException) { Check(true, "mutable and model-selected image names refused"); }
Environment.SetEnvironmentVariable("OPENAI_API_KEY", "synthetic-secret-not-for-child");
Environment.SetEnvironmentVariable("CONTAINER_HOST", "unix:///tmp/unsafe.sock");
var startInfo = SandboxPolicy.ProcessInfo(sandboxArgs);
Check(startInfo.FileName == "/usr/bin/podman" && !startInfo.UseShellExecute &&
    !startInfo.Environment.ContainsKey("OPENAI_API_KEY") && !startInfo.Environment.ContainsKey("CONTAINER_HOST"),
    "container launcher does not inherit API keys or remote socket configuration");
string Inspection(bool privileged = false, bool bind = false, string network = "none", bool readOnly = true) =>
    System.Text.Json.JsonSerializer.Serialize(new[] { new {
        Name = sandboxName, Image = sandboxImage[7..], Path = "/usr/local/bin/archer-verify",
        EffectiveCaps = Array.Empty<string>(), BoundingCaps = Array.Empty<string>(),
        Config = new { User = "65532:65532" },
        HostConfig = new { NetworkMode = network, Privileged = privileged, ReadonlyRootfs = readOnly,
            Memory = 1536L * 1024 * 1024, PidsLimit = 128, PidMode = "private", IpcMode = "private",
            SecurityOpt = new[] { "no-new-privileges" }, Binds = Array.Empty<string>() },
        Mounts = bind ? new[] { new { Type = "bind", Destination = "/host" } } :
            new[] { new { Type = "tmpfs", Destination = "/work" }, new { Type = "tmpfs", Destination = "/tmp" } }
    } });
Check(SandboxPolicy.InspectSafe(Inspection(), sandboxName, sandboxImage), "expected inspected container policy accepted");
Check(!SandboxPolicy.InspectSafe(Inspection(privileged: true), sandboxName, sandboxImage) &&
    !SandboxPolicy.InspectSafe(Inspection(bind: true), sandboxName, sandboxImage) &&
    !SandboxPolicy.InspectSafe(Inspection(network: "host"), sandboxName, sandboxImage) &&
    !SandboxPolicy.InspectSafe(Inspection(readOnly: false), sandboxName, sandboxImage) &&
    !SandboxPolicy.InspectSafe("[]", sandboxName, sandboxImage),
    "privileged, mounted, network-enabled, mutable, or incomplete inspect results refused");
var safeDiagnostic = SandboxPolicy.Evidence(new string('a',40), sandboxImage, 10, "fake-key-do-not-store CS1002 RZ10001");
Check(safeDiagnostic.Status == "Failed" && safeDiagnostic.Detail.Contains("CS1002") &&
    !safeDiagnostic.Detail.Contains("fake-key-do-not-store"), "diagnostics persist compiler codes only");
Check(SandboxPolicy.Evidence(new string('a',40), sandboxImage, 137, "secret").Status == "Blocked" &&
    SandboxPolicy.Evidence(new string('a',40), sandboxImage, 0, "").Status == "Passed", "only known runner outcomes classified");
Check(SandboxArchiveReader.AllowedRedirect(new Uri("https://codeload.github.com/owner/future/legacy.tar.gz/abc?token=opaque"), other.Repository) &&
    !SandboxArchiveReader.AllowedRedirect(new Uri("https://evil.example/owner/future/abc"), other.Repository) &&
    !SandboxArchiveReader.AllowedRedirect(new Uri("https://codeload.github.com/owner/other/abc"), other.Repository),
    "archive redirects restricted to exact repository on expected GitHub CDN");
var archiveHandler = new FakeSandboxArchive(other.Repository);
using var archiveHttp = new HttpClient(archiveHandler);
var archiveReader = new SandboxArchiveReader(archiveHttp);
var archiveBytes = await archiveReader.DownloadAsync(other, new string('a',40), default);
Check(archiveBytes.Length == 3 && archiveHandler.Requests == 2, "bounded archive received without sending scoped token to codeload");
archiveHandler.Oversized = true;
try { await archiveReader.DownloadAsync(other, new string('a',40), default); throw new Exception("Oversized archive accepted"); }
catch (InvalidOperationException) { Check(true, "oversized compressed archive rejected"); }
archiveHandler.Oversized = false;

if (Environment.GetEnvironmentVariable("ARCHER_SANDBOX_SMOKE_IMAGE") is { Length: > 0 } smokeImage)
{
    Environment.SetEnvironmentVariable("ARCHER_SANDBOX_ENABLED", "true");
    Environment.SetEnvironmentVariable(prefix + "_SANDBOX_IMAGE", smokeImage);
    other.RequireSandbox = true;
    var runner = new ProjectSandboxRunner(archiveReader);
    var smokeEvidence = await runner.VerifyAsync(other, new CodeProposal { Repository = other.Repository,
        PublishedHeadSha = new string('a',40) }, default);
    Check(smokeEvidence.Status == "Passed", "actual rootless Podman fixture: " + smokeEvidence.Detail);
}
Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
Environment.SetEnvironmentVariable("CONTAINER_HOST", null);

// A continuous objective consumes one total authorization across multiple cycles.
var continuousProject = ProjectPolicy.Register(new("Continuous app", "owner/continuous", ".NET",
    ["src"], "ci.yml", "Build", ["Compile", "Test"], 3, 12, true, true));
var continuousObjective = new ProjectObjective
{
    Description = "Implement the assigned feature with regression coverage",
    Continuous = true, MaxPlanningCycles = 3, MaxCodingAttempts = 4, PlanningCyclesUsed = 1,
    PlanningAttempted = true, PlanningState = "Batch created", DelegatedUntilUtc = now.AddHours(4)
};
continuousProject.Objectives.Add(continuousObjective);
var firstHead = new string('a', 40);
var continuousBatch = new DevelopmentBatch
{
    ProjectId = continuousProject.Id, ObjectiveId = continuousObjective.Id, Cumulative = true,
    Continuous = true, PlanningCycles = 1, ExpiresAtUtc = continuousObjective.DelegatedUntilUtc,
    WorkspaceHeadSha = firstHead, WorkspaceBranch = "archer/draft-123456789abc",
    WorkspacePullRequestNumber = 42, WorkspaceBaseBranch = "main",
    Tasks = [new DevelopmentTask { Instruction = "First verified change", Status = "Verified", Attempts = 2,
        Proposal = new CodeProposal { Repository = continuousProject.Repository, PublishedHeadSha = firstHead, BranchName = "archer/draft-123456789abc", BaseBranch = "main", PullRequestNumber = 42 } }]
};
Check(ContinuousObjectivePolicy.RemainingAttempts(continuousObjective, continuousBatch) == 2, "continuous allowance counts earlier-cycle repairs");
ContinuousObjectivePolicy.Reconcile(continuousProject, continuousObjective, continuousBatch, now);
Check(continuousObjective.PlanningState == "Reassessment queued", "only fully verified cycle queues reassessment");
Check(ContinuousObjectivePolicy.Claim(continuousProject, continuousObjective, continuousBatch, now), "continuous planning reserves next call");
Check(!ContinuousObjectivePolicy.Claim(continuousProject, continuousObjective, continuousBatch, now), "duplicate reassessment refused");
Check(!ContinuousObjectivePolicy.Append(continuousProject, continuousObjective, continuousBatch, [new DevelopmentTask { Instruction = "First verified change" }], 5, now), "exact duplicate task cannot consume another cycle");
var extraTasks = new[] { new DevelopmentTask { Instruction = "Next change" } };
Check(!ContinuousObjectivePolicy.Append(continuousProject, continuousObjective, continuousBatch, extraTasks, 0, now), "reassessment cannot overrun project quota");
Check(ContinuousObjectivePolicy.Append(continuousProject, continuousObjective, continuousBatch, extraTasks, 5, now), "verified workspace gains another cycle");
Check(continuousBatch.Armed && continuousBatch.Tasks.Count == 2 && continuousBatch.PlanningCycles == 2 &&
    continuousBatch.WorkspaceHeadSha == firstHead && continuousBatch.WorkspacePullRequestNumber == 42 &&
    continuousBatch.AttemptsRemaining == 2 && continuousBatch.ExpiresAtUtc == now.AddHours(4), "same draft, history, deadline and total budget preserved");
Check(!ContinuousObjectivePolicy.Append(continuousProject, continuousObjective, continuousBatch, extraTasks, 5, now), "append cannot be replayed");
Check(DevelopmentBatchPolicy.Claim(continuousBatch, extraTasks[0], now), "next cycle executes in order");
extraTasks[0].Status = "Testing";
extraTasks[0].Proposal = new CodeProposal { Repository = continuousProject.Repository, PublishedHeadSha = new string('b',40), BranchName = continuousBatch.WorkspaceBranch, BaseBranch = "main", PullRequestNumber = 42 };
continuousBatch.WorkspaceHeadSha = extraTasks[0].Proposal.PublishedHeadSha;
DevelopmentBatchPolicy.CompleteCi(continuousBatch, extraTasks[0], "Verified", "passed");
ContinuousObjectivePolicy.Reconcile(continuousProject, continuousObjective, continuousBatch, now);
Check(ContinuousObjectivePolicy.Claim(continuousProject, continuousObjective, continuousBatch, now), "third and final permitted planning cycle reserved");
var third = new DevelopmentTask { Instruction = "Final change" };
Check(ContinuousObjectivePolicy.Append(continuousProject, continuousObjective, continuousBatch, [third], 4, now) &&
    continuousBatch.AttemptsRemaining == 1, "only final unused attempt granted");
third.Status = "Testing"; third.Attempts = 1;
third.Proposal = new CodeProposal { Repository = continuousProject.Repository, PublishedHeadSha = new string('c',40), BranchName = continuousBatch.WorkspaceBranch, BaseBranch = "main", PullRequestNumber = 42 };
continuousBatch.WorkspaceHeadSha = third.Proposal.PublishedHeadSha;
DevelopmentBatchPolicy.CompleteCi(continuousBatch, third, "Verified", "passed");
ContinuousObjectivePolicy.Reconcile(continuousProject, continuousObjective, continuousBatch, now);
Check(continuousObjective.PlanningState == "Review required" && continuousObjective.DelegatedUntilUtc is null &&
    continuousObjective.AcceptedAtUtc is null, "limits stop work without self-acceptance or renewal");
Check(!ContinuousObjectivePolicy.Claim(continuousProject, continuousObjective, continuousBatch, now), "no calls after exhaustion");
Check(ContinuousObjectivePolicy.History(continuousBatch).Contains("First verified change"), "reassessment has verified evidence");
var interruptedContinuous = new ProjectObjective { Continuous = true, PlanningState = "Planning", PlanningCyclesUsed = 2, DelegatedUntilUtc = now.AddHours(1) };
ProjectPolicy.RecoverDelegation(interruptedContinuous, now);
Check(interruptedContinuous.PlanningState == "Blocked" && interruptedContinuous.DelegatedUntilUtc is null, "interrupted reassessment is not retried");
interruptedContinuous.PlanningState = "Reassessment queued"; interruptedContinuous.DelegatedUntilUtc = now.AddHours(1);
ProjectPolicy.RecoverDelegation(interruptedContinuous, now);
Check(interruptedContinuous.PlanningState == "Blocked", "restart without permission stops future reassessment");

// Protected self-development is a distinct opt-in, not a generic project bypass.
var selfProfile = ProjectPolicy.Register(SelfDevelopmentPolicy.Profile());
Environment.SetEnvironmentVariable("ARCHER_SELF_PROJECT_ENABLED", null);
Environment.SetEnvironmentVariable("ARCHER_SELF_PROJECT_FILES", null);
Check(!SelfDevelopmentPolicy.Configured(selfProfile), "self-project disabled by default");
Check(!SelfDevelopmentPolicy.CanEdit("gravybets/archer", SelfDevelopmentPolicy.PilotPaths[0]), "self repository case-insensitive gate");
Environment.SetEnvironmentVariable("ARCHER_SELF_PROJECT_ENABLED", "true");
Environment.SetEnvironmentVariable("ARCHER_SELF_PROJECT_FILES", SelfDevelopmentPolicy.PilotPaths[0] + ",Archer.MissionControl/Program.cs,.github/workflows/build.yml");
Check(SelfDevelopmentPolicy.Configured(selfProfile), "explicit pilot file enables reviewed profile");
Check(SelfDevelopmentPolicy.ApprovedPaths.Count == 1, "external opt-in cannot expand baked-in self scope");
Check(!SelfDevelopmentPolicy.CanEdit(selfProfile.Repository, "Archer.MissionControl/Program.cs"), "core permissions not writable");
Check(!SelfDevelopmentPolicy.CanEdit(selfProfile.Repository, SelfDevelopmentPolicy.PilotPaths[1]), "non-approved pilot file not writable");
Check(!ProjectPolicy.NewSourceAllowed(selfProfile, SelfDevelopmentPolicy.Root + "/new.js"), "self-project cannot create new source files");
var selfState = new ArcherState();
Check(SelfDevelopmentReview.Audit(selfState).Any(f => f.Status == "Setup required"), "unregistered self-project is reported");
selfState.Projects.Add(selfProfile);
var selfBatch = new DevelopmentBatch
{
    ProjectId = selfProfile.Id,
    Cumulative = true,
    WorkspaceBaseBranch = "main",
    WorkspaceBranch = "archer/draft-test",
    WorkspaceOriginalBaseCommitSha = new string('a',40),
    WorkspaceHeadSha = new string('b',40),
    WorkspacePullRequestNumber = 123,
    Tasks = [new DevelopmentTask { Status = "Verified", Proposal = new CodeProposal
    {
        Repository = selfProfile.Repository, PublishedHeadSha = new string('b',40),
        BranchName = "archer/draft-test", BaseBranch = "main", PullRequestNumber = 123,
        Files = [new CodeProposalFile { Path = SelfDevelopmentPolicy.PilotPaths[0] }]
    } }]
};
selfState.DevelopmentBatches.Add(selfBatch);
var candidate = SelfDevelopmentReview.TryCandidate(selfProfile, selfBatch);
Check(candidate is not null && candidate.BaselineSha == new string('a',40) && candidate.HeadSha == new string('b',40), "candidate retains immutable source baseline and draft head");
selfBatch.Armed = true;
Check(SelfDevelopmentReview.TryCandidate(selfProfile, selfBatch) is null, "running self-work cannot be handed off");
selfBatch.Armed = false;
selfBatch.Tasks[0].Proposal!.Files[0].Path = "Archer.MissionControl/Program.cs";
Check(SelfDevelopmentReview.TryCandidate(selfProfile, selfBatch) is null, "protected source invalidates candidate");
Environment.SetEnvironmentVariable("ARCHER_SELF_PROJECT_ENABLED", null);
Environment.SetEnvironmentVariable("ARCHER_SELF_PROJECT_FILES", null);

sealed class FakeGitHub : HttpMessageHandler
{
    public string Mode = "success";
    public bool Draft = true;
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
            payload = new { state = "open", draft = Draft, head = new { sha, @ref = "archer/draft-example",
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
            if (path == "/git/ref/heads/master") payload = new { @object = new { sha = h('a') } };
            else if (path == "/git/ref/heads/" + CumulativeBranch) payload = new { @object = new { sha = h('e') } };
            else if (path == "/pulls/9") payload = new { state = "open", draft = DraftOpen, head = new { @ref = CumulativeBranch, sha = ExternalChange ? h('f') : h('e'), repo = new { full_name = repository } }, @base = new { @ref = "master" } };
            else if (path == "/git/commits/" + h('e')) payload = new { tree = new { sha = h('d') } };
            else if (path == "/git/trees/" + h('d')) payload = new { truncated = false, tree = new[] {
                new { path = "src", mode = "040000", type = "tree", sha = h('f') },
                new { path = "src/components", mode = "040000", type = "tree", sha = h('f') },
                new { path = "src/components/Map.tsx", mode = "100644", type = "blob", sha = h('1') },
                new { path = "src/components/MapState.ts", mode = "100644", type = "blob", sha = h('2') }
            } };
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
                if (root.GetProperty("base_tree").GetString() != h(CumulativeBranch is null ? 'b' : 'd') || root.GetProperty("tree").GetArrayLength() != (CumulativeBranch is null ? 2 : 1))
                    throw new Exception("Tree not atomic");
                payload = new { sha = h(CumulativeBranch is null ? 'd' : '3') };
            }
            else if (path == "/git/commits")
            {
                if (root.GetProperty("tree").GetString() != h(CumulativeBranch is null ? 'd' : '3') || root.GetProperty("parents")[0].GetString() != h(CumulativeBranch is null ? 'a' : 'e'))
                    throw new Exception("Wrong commit ancestry");
                payload = new { sha = h(CumulativeBranch is null ? 'e' : '4') };
            }
            else if (path == "/git/refs")
            {
                if (!(root.GetProperty("ref").GetString() ?? "").StartsWith("refs/heads/archer/draft-") ||
                    root.GetProperty("sha").GetString() != h('e')) throw new Exception("Unsafe branch");
                payload = new { };
            }
            else if (path == "/git/refs/heads/" + CumulativeBranch)
            {
                if (root.GetProperty("force").GetBoolean() || root.GetProperty("sha").GetString() != h('4')) throw new Exception("Not a safe fast-forward");
                payload = new { @object = new { sha = h('4') } };
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

sealed class FakeSandboxArchive(string repository) : HttpMessageHandler
{
    public int Requests;
    public bool Oversized;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        var url = request.RequestUri!;
        if (url.Host == "api.github.com")
        {
            if (request.Headers.Authorization?.Parameter != "synthetic-reader" ||
                !url.AbsolutePath.StartsWith("/repos/" + repository + "/tarball/"))
                throw new Exception("Invalid source authorization");
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://codeload.github.com/" + repository + "/legacy.tar.gz/abc");
            return Task.FromResult(response);
        }
        if (url.Host != "codeload.github.com" || request.Headers.Authorization is not null)
            throw new Exception("Scope credential leaked across redirect");
        var content = new ByteArrayContent(new byte[] { 0x1f, 0x8b, 0x00 });
        if (Oversized) content.Headers.ContentLength = SandboxPolicy.MaximumArchiveBytes + 1L;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
