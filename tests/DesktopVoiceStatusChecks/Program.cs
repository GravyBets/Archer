using System.Text.Json;
using Archer.MissionControl.Services;

var count = 0;
void Check(bool condition, string description)
{
    count++;
    if (!condition) throw new Exception("FAIL: " + description);
}

var root = Path.Combine(Path.GetTempPath(), "archer-desktop-voice-test-" +
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var path = Path.Combine(root, "desktop-voice-status.json");
    Check(!ArcherDesktopVoiceStatus.Read(root).Running,
        "no native companion process means no implied active microphone");

    void Write(string status, DateTime atUtc, string detail = "Test state")
    {
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            status, detail,
            updatedUtc = atUtc.ToString("o"), pid = Environment.ProcessId
        }));
    }

    Write("standby", DateTime.UtcNow);
    var standby = ArcherDesktopVoiceStatus.Read(root);
    Check(standby.Running && standby.Status == "standby",
        "fresh Windows tray heartbeat reports wake-only standby");

    Write("awake", DateTime.UtcNow);
    Check(ArcherDesktopVoiceStatus.Read(root).Running,
        "fresh actual wake event can display a live conversation");

    Write("paused", DateTime.UtcNow);
    Check(ArcherDesktopVoiceStatus.Read(root).Status == "paused",
        "paused microphone remains visibly paused instead of listening");

    Write("thinking", DateTime.UtcNow.AddSeconds(-25));
    Check(ArcherDesktopVoiceStatus.Read(root).Running,
        "long model request does not falsely claim native companion crashed");

    Write("standby", DateTime.UtcNow.AddSeconds(-25));
    Check(!ArcherDesktopVoiceStatus.Read(root).Running,
        "stale wake-only heartbeat never claims active microphone capture");

    Write("error", DateTime.UtcNow.AddDays(-2),
        "No installed Windows English Speech Recognition engine.");
    var error = ArcherDesktopVoiceStatus.Read(root);
    Check(!error.Running && error.Status == "error" &&
          error.Detail.Contains("English Speech"),
        "missing Windows language pack stays visible for troubleshooting");

    File.WriteAllText(path, "not json");
    Check(!ArcherDesktopVoiceStatus.Read(root).Running,
        "malformed untrusted heartbeat is not trusted");

    File.WriteAllText(path, new string('x', 8000));
    Check(!ArcherDesktopVoiceStatus.Read(root).Running,
        "oversized status file rejected without parsing");

    Console.WriteLine($"PASS: {count} local Windows wake listener heartbeat checks.");
}
finally { Directory.Delete(root, recursive: true); }
