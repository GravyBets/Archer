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
var verifier = new GeoShiftCiVerifier(http);
var proposal = new CodeProposal { Repository = "GravyBets/GeoShift", BaseBranch = "master",
    BranchName = "archer/draft-example", PublishedHeadSha = new string('a',40), PullRequestNumber = 7 };
Check(await verifier.ReadyAsync(default), "expected workflow present");
Check((await verifier.InspectAsync(proposal, default)).Status == "Verified", "exact-head successful build and tests");
fake.Mode = "failed";
Check((await verifier.InspectAsync(proposal, default)).Status == "Failed", "failed build is not green");
fake.Mode = "skipped";
Check((await verifier.InspectAsync(proposal, default)).Status == "Blocked", "skipped tests never verify");
fake.Mode = "pending";
Check((await verifier.InspectAsync(proposal, default)).Status == "Testing", "missing run never verifies");
fake.Mode = "changed";
Check((await verifier.InspectAsync(proposal, default)).Status == "Blocked", "changed PR head refused");

sealed class FakeGitHub : HttpMessageHandler
{
    public string Mode = "success";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var sha = new string(Mode == "changed" ? 'b' : 'a',40);
        var path = request.RequestUri!.AbsolutePath;
        string json;
        if (path.EndsWith("/pulls/7"))
            json = $$"""{"state":"open","head":{"sha":"{{sha}}","ref":"archer/draft-example","repo":{"full_name":"GravyBets/GeoShift"}},"base":{"ref":"master"}}""";
        else if (path.EndsWith("/archer-ci.yml"))
            json = """{"state":"active","path":".github/workflows/archer-ci.yml"}""";
        else if (path.EndsWith("/archer-ci.yml/runs"))
            json = Mode == "pending" ? """{"workflow_runs":[]}""" :
                $$"""{"workflow_runs":[{"id":10,"head_sha":"{{sha}}","head_branch":"archer/draft-example","event":"pull_request","status":"completed","conclusion":"{{(Mode == "failed" ? "failure" : "success")}}"}]}""";
        else if (path.EndsWith("/runs/10/jobs"))
            json = $$"""{"jobs":[{"name":"GeoShift build and tests","conclusion":"{{(Mode == "failed" ? "failure" : "success")}}","steps":[{"name":"Build Web","conclusion":"{{(Mode == "failed" ? "failure" : "success")}}"},{"name":"Run core regression tests","conclusion":"{{(Mode == "skipped" || Mode == "failed" ? "skipped" : "success")}}"}]}]}""";
        else throw new Exception("Unexpected CI request: " + path);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
