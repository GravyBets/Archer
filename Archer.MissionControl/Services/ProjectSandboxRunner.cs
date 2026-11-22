using System.Diagnostics;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public sealed class ProjectSandboxRunner(SandboxArchiveReader archiveReader)
{
    public async Task<SandboxEvidence> VerifyAsync(ManagedProject project, CodeProposal proposal, CancellationToken ct)
    {
        var head = proposal.PublishedHeadSha ?? "";
        var image = SandboxPolicy.ImageFor(project) ?? "";
        SandboxEvidence Blocked(string reason) => new() { HeadSha = head, ImageId = image, Status = "Blocked", Detail = reason };
        if (!project.RequireSandbox || !SandboxPolicy.Configured(project) ||
            !SandboxPolicy.IsHead(head) || !string.Equals(project.Repository, proposal.Repository, StringComparison.OrdinalIgnoreCase))
            return Blocked("Isolated verification is required but is not correctly configured.");
        var name = "archer-verify-" + proposal.Id.ToString("N");
        bool created = false;
        SandboxEvidence? evidence = null;
        try
        {
            var rootless = await RunAsync(["--remote=false", "info", "--format", "{{.Host.Security.Rootless}} {{.Host.CgroupsVersion}}"], null, 20, ct);
            if (rootless.ExitCode != 0 || rootless.Output.Trim() != "true v2")
                return Blocked("Sandbox requires local rootless Podman with cgroup v2.");
            using var download = CancellationTokenSource.CreateLinkedTokenSource(ct);
            download.CancelAfter(TimeSpan.FromSeconds(90));
            var archive = await archiveReader.DownloadAsync(project, head, download.Token);
            var create = await RunAsync(SandboxPolicy.CreateArguments(image, name), null, 30, ct);
            if (create.ExitCode != 0)
                return Blocked("Container creation failed. Check the pinned local image and runtime; no code executed.");
            created = true;
            var inspect = await RunAsync(["--remote=false", "container", "inspect", name], null, 20, ct);
            if (inspect.ExitCode != 0 || !SandboxPolicy.InspectSafe(inspect.Output, name, image))
                evidence = Blocked("Container security inspection failed. Generated code was not executed.");
            else
            {
                var execution = await RunAsync(["--remote=false", "start", "--attach", "--interactive", name],
                    archive, SandboxPolicy.TimeoutSeconds + 15, ct);
                evidence = SandboxPolicy.Evidence(head, image, execution.ExitCode, execution.Output + "\n" + execution.Error);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        { evidence = Blocked("Isolated verification exceeded its time allowance."); }
        catch (Exception)
        { evidence = Blocked("Isolated verification infrastructure failed; no raw diagnostics retained."); }
        finally
        {
            if (created)
            {
                try
                {
                    var removed = await RunAsync(["--remote=false", "rm", "--force", "--time=2", name], null, 15, CancellationToken.None);
                    if (removed.ExitCode != 0) evidence = Blocked("Container cleanup failed. Inspect the dedicated sandbox host before retrying.");
                }
                catch (Exception)
                { evidence = Blocked("Container cleanup failed. Inspect the dedicated sandbox host before retrying."); }
            }
        }
        return evidence ?? Blocked("No isolated verification result was recorded.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
    private static async Task<ProcessResult> RunAsync(IReadOnlyList<string> args, byte[]? input, int timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        using var process = new Process { StartInfo = SandboxPolicy.ProcessInfo(args) };
        if (!process.Start()) throw new InvalidOperationException("Podman could not start.");
        using var kill = deadline.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });
        var output = DrainAsync(process.StandardOutput, deadline.Token);
        var error = DrainAsync(process.StandardError, deadline.Token);
        var write = FeedAsync(process.StandardInput.BaseStream, input, deadline.Token);
        await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error, write);
        deadline.Token.ThrowIfCancellationRequested();
        return new(process.ExitCode, output.Result, error.Result);
    }

    private static async Task FeedAsync(Stream destination, byte[]? input, CancellationToken ct)
    {
        try
        {
            if (input is not null) await destination.WriteAsync(input.AsMemory(), ct);
            await destination.FlushAsync(ct);
        }
        catch (IOException) { /* early exit still requires the actual status */ }
        finally { await destination.DisposeAsync(); }
    }

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken ct)
    {
        // Continue draining so verbose builds never deadlock on full pipes.
        // Retain only a bounded prefix in memory; never persist arbitrary logs.
        const int cap = 128 * 1024;
        var builder = new System.Text.StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) break;
            var take = Math.Min(count, cap - builder.Length);
            if (take > 0) builder.Append(buffer, 0, take);
        }
        return builder.ToString();
    }
}
