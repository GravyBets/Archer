using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public static class SandboxPolicy
{
    public const string Executable = "/usr/bin/podman";
    public const int MaximumArchiveBytes = 32 * 1024 * 1024;
    public const int TimeoutSeconds = 480;
    public static bool Enabled => Environment.GetEnvironmentVariable("ARCHER_SANDBOX_ENABLED") == "true";
    public static bool IsImageId(string? image) => Regex.IsMatch(image ?? "", @"^sha256:[a-f0-9]{64}$");
    public static bool IsHead(string? head) => Regex.IsMatch(head ?? "", @"^[a-fA-F0-9]{40}$");
    public static string? ImageFor(ManagedProject project) =>
        Environment.GetEnvironmentVariable(ProjectGitHubCredentials.Prefix(project.Repository) + "_SANDBOX_IMAGE");
    public static bool Configured(ManagedProject project) =>
        !project.RequireSandbox || (Enabled && OperatingSystem.IsLinux() && File.Exists(Executable) && IsImageId(ImageFor(project)));

    public static bool IsName(string name) => Regex.IsMatch(name, @"^archer-verify-[a-f0-9]{32}$");

    public static IReadOnlyList<string> CreateArguments(string image, string name)
    {
        if (!IsImageId(image) || !IsName(name)) throw new ArgumentException("Expected a pinned local image ID and generated container identity.");
        return [
            "--remote=false", "create", "--name=" + name, "--interactive",
            "--pull=never", "--network=none", "--http-proxy=false", "--no-healthcheck",
            "--pid=private", "--ipc=private", "--uts=private",
            "--userns=keep-id:uid=65532,gid=65532", "--user=65532:65532",
            "--cap-drop=ALL", "--security-opt=no-new-privileges",
            "--read-only", "--read-only-tmpfs=false", "--image-volume=ignore", "--systemd=false",
            "--pids-limit=128", "--memory=1536m", "--memory-swap=1536m", "--cpus=2",
            "--timeout=" + TimeoutSeconds, "--stop-timeout=2", "--restart=no", "--log-driver=none",
            "--tmpfs=/work:rw,exec,nosuid,nodev,size=768m,mode=1777,notmpcopyup",
            "--tmpfs=/tmp:rw,noexec,nosuid,nodev,size=128m,mode=1777,notmpcopyup",
            "--env=HOME=/work", "--env=TMPDIR=/tmp", "--env=DOTNET_CLI_HOME=/work",
            "--env=DOTNET_CLI_TELEMETRY_OPTOUT=1", "--env=DOTNET_NOLOGO=1",
            "--entrypoint=/usr/local/bin/archer-verify", image
        ];
    }

    public static ProcessStartInfo ProcessInfo(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = "/"
        };
        // Never inherit Archer's OpenAI, GitHub, Auth0 or other integration credentials,
        // proxy variables, container socket settings, PATH overrides, or arbitrary env.
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";
        start.Environment["LANG"] = "C.UTF-8";
        foreach (var key in new[] { "HOME", "XDG_RUNTIME_DIR" })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)) start.Environment[key] = value;
        }
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        return start;
    }

    // Podman inspect is trusted runtime metadata, not stdout from generated code.
    // Fail closed on missing security fields, unexpected mounts, or incompatible runtime.
    public static bool InspectSafe(string json, string name, string image)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement[0];
            var config = root.GetProperty("Config");
            var host = root.GetProperty("HostConfig");
            return root.GetProperty("Name").GetString()?.TrimStart('/') == name &&
                NormalizeImage(root.GetProperty("Image").GetString()) == image &&
                config.GetProperty("User").GetString() == "65532:65532" &&
                host.GetProperty("NetworkMode").GetString() == "none" &&
                !host.GetProperty("Privileged").GetBoolean() &&
                host.GetProperty("ReadonlyRootfs").GetBoolean() &&
                host.GetProperty("Memory").GetInt64() > 0 &&
                host.GetProperty("Memory").GetInt64() <= 1536L * 1024 * 1024 &&
                host.GetProperty("PidsLimit").GetInt64() is > 0 and <= 128 &&
                root.GetProperty("Mounts").EnumerateArray().All(m =>
                    m.GetProperty("Type").GetString() == "tmpfs" &&
                    m.GetProperty("Destination").GetString() is "/work" or "/tmp") &&
                (!host.TryGetProperty("Binds", out var binds) || binds.ValueKind == JsonValueKind.Null ||
                    (binds.ValueKind == JsonValueKind.Array && binds.GetArrayLength() == 0));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        { return false; }
    }
    private static string? NormalizeImage(string? image) =>
        image is null || image.StartsWith("sha256:", StringComparison.Ordinal) ? image : "sha256:" + image;

    public static SandboxEvidence Evidence(string head, string image, int exitCode, string output)
    {
        // Raw logs are treated as untrusted, may contain project data, and never
        // enter memory or model prompts. Only allow known compiler-code shapes.
        var codes = Regex.Matches(output, @"\b(?:CS|RZ|TS)[0-9]{4,5}\b")
            .Select(m => m.Value).Distinct().Take(8).ToArray();
        var suffix = codes.Length == 0 ? "" : " Compiler codes: " + string.Join(", ", codes) + ".";
        return new SandboxEvidence
        {
            HeadSha = head, ImageId = image,
            Status = exitCode == 0 ? "Passed" : exitCode is 10 or 20 ? "Failed" : "Blocked",
            Detail = exitCode switch
            {
                0 => "Isolated image entrypoint reported successful build and tests. Independent CI is still required.",
                10 => "Isolated build reported failure." + suffix,
                20 => "Isolated tests reported failure." + suffix,
                _ => "Isolated runner did not complete the verification contract (exit " + exitCode + ")."
            }
        };
    }
}
