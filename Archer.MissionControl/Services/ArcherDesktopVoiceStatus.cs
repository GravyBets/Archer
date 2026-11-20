using System.Diagnostics;
using System.Text.Json;

namespace Archer.MissionControl.Services;

/// <summary>
/// Read-only display of the owner's separately launched Windows tray listener.
/// The heartbeat is not an authentication token or a control channel.
/// It contains no raw microphone audio, utterance text or personal secrets.
/// </summary>
internal static class ArcherDesktopVoiceStatus
{
    internal sealed record Snapshot(
        bool Running, string Status, string Detail, DateTime? UpdatedUtc);

    internal static Snapshot Read(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return new(false, "offline", "Desktop voice has not started.", null);

        var path = Path.Combine(dataDirectory, "desktop-voice-status.json");
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 4096)
                return new(false, "offline", "Windows voice listener is not running.", null);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var status = root.GetProperty("status").GetString() ?? "offline";
            var detail = root.GetProperty("detail").GetString() ?? "";
            if (detail.Length > 200) detail = detail[..200];
            if (!DateTime.TryParse(root.GetProperty("updatedUtc").GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var timestamp))
                return new(false, "offline", "Desktop voice heartbeat is invalid.", null);

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "starting", "standby", "awake", "thinking", "speaking",
                "paused", "error", "stopped"
            };
            if (!allowed.Contains(status))
                return new(false, "offline", "Desktop voice reported an unknown state.", timestamp);

            // Paused/error states provide useful troubleshooting evidence even
            // when the process has already exited. Do not claim it is listening.
            var maximumAge = status is "thinking" or "speaking"
                ? TimeSpan.FromMinutes(5)
                : TimeSpan.FromSeconds(12);
            if (timestamp > DateTime.UtcNow.AddSeconds(30) ||
                DateTime.UtcNow - timestamp > maximumAge)
                return new(false, status is "error" or "stopped" ? status : "offline",
                    status is "error" ? detail : "Desktop voice listener is not running.", timestamp);

            if (status is "error" or "stopped")
                return new(false, status, detail, timestamp);

            if (root.TryGetProperty("pid", out var processId) &&
                processId.TryGetInt32(out var pid) && pid > 0 &&
                OperatingSystem.IsWindows())
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (process.HasExited)
                        return new(false, "offline", "Desktop voice process exited.", timestamp);
                }
                catch (ArgumentException)
                {
                    return new(false, "offline", "Desktop voice process exited.", timestamp);
                }
                catch (InvalidOperationException)
                {
                    return new(false, "offline", "Desktop voice process exited.", timestamp);
                }
            }

            return new(true, status, detail, timestamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new(false, "offline", "Desktop voice status unavailable.", null);
        }
    }
}
