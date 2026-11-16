using System.Security.Cryptography;
using System.Text;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Future companion entry point. OFF unless a long random bearer token is
/// explicitly configured. The current app binds to loopback only: no mobile
/// device can reach it remotely as-is.
/// </summary>
public static class MobilePresenceIngress
{
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("ARCHER_MOBILE_PRESENCE_ENABLED") == "true" &&
        (Environment.GetEnvironmentVariable("ARCHER_MOBILE_PRESENCE_TOKEN")?.Length ?? 0) >= 32;

    public static bool Authorized(string? bearer)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(bearer) ||
            !bearer.StartsWith("Bearer ", StringComparison.Ordinal))
            return false;

        var provided = bearer["Bearer ".Length..];
        var expected = Environment.GetEnvironmentVariable("ARCHER_MOBILE_PRESENCE_TOKEN") ?? "";
        if (provided.Length != expected.Length) return false;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static OwnerPresence Convert(MobilePresenceRequest request)
    {
        var allowed = new[] { "Available", "Driving", "Working", "Shopping", "Busy", "Other" };
        var activity = request.Activity?.Trim() ?? "";
        var place = request.PlaceLabel?.Trim() ?? "";
        if (!allowed.Contains(activity, StringComparer.Ordinal) ||
            place.Length > 60 || request.DurationMinutes is not (15 or 60 or 120))
            throw new ArgumentException("Choose a supported activity, <=60-character general place, and expiry 15, 60 or 120 minutes.");

        var now = DateTimeOffset.UtcNow;
        return new OwnerPresence
        {
            Activity = activity,
            PlaceLabel = place,
            ApproxLatitude = null, ApproxLongitude = null,
            ShareWithAi = request.ShareWithAi,
            UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(request.DurationMinutes)
        };
    }
}
