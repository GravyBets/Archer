using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Archer.RemoteGateway;

/// <summary>
/// Auth0-verified owner + device-key proof + separate operator authorization.
/// EVERY route stays on loopback, in Development, and behind signed Auth0
/// bearer verification. Browsers on other origins cannot reach this service.
///
/// These are local proof-of-integration routes, not the public iPhone API.
/// Pairing/approval and revocation are completely independent from missions,
/// chat, AI provider access, finance, GitHub, and telephone services.
/// </summary>
internal static class LocalPairingRoutes
{
    internal sealed record BeginRequest(string? Label, string? PublicSpkiBase64Url);
    internal sealed record ProofRequest(string? TicketId, string? SignatureBase64Url);
    internal sealed record TicketRequest(string? TicketId);
    internal sealed record RevokeRequest(string? DeviceId);

    internal static void Map(
        WebApplication app, Auth0OwnerSignInSettings owner,
        LocalPairingSettings settings)
    {
        var registry = new DurableDeviceRegistry(
            settings.RegistryPath, createIfMissing: true);
        var coordinator = new OwnerEnrollmentCoordinator(
            registry, settings.OperatorSecret);

        var group = app.MapGroup("/_local/pairing").RequireAuthorization();
        group.MapPost("/begin", (HttpContext context, BeginRequest request) =>
        {
            if (!Auth0OwnerSignInProbe.IsValidatedOwner(context))
                return Results.Forbid();
            if (request.Label is not { Length: >= 2 and <= 80 } label ||
                label.Any(char.IsControl) ||
                !TryDecode(request.PublicSpkiBase64Url, 64, 256, out var spki))
                return Results.BadRequest(new { error = "Invalid pairing request." });
            try
            {
                var challenge = coordinator.Begin(
                    owner.OwnerSubject, label, spki, DateTimeOffset.UtcNow);
                return Results.Ok(new
                {
                    ticketId = challenge.TicketId,
                    nonce = challenge.Nonce,
                    expiresAtUtc = challenge.ExpiresAtUtc,
                    ownerSubject = owner.OwnerSubject,
                    deviceEnrolled = false,
                    remoteAccessEnabled = false
                });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "Invalid device public key." });
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
        });
        group.MapPost("/prove", (HttpContext context, ProofRequest request) =>
        {
            if (!Auth0OwnerSignInProbe.IsValidatedOwner(context))
                return Results.Forbid();
            if (request.TicketId is not { Length: 48 } ticket ||
                !IsHex(ticket, 48) ||
                !TryDecode(request.SignatureBase64Url, 64, 64, out var signature))
                return Results.BadRequest(new { error = "Invalid pairing proof." });
            var success = coordinator.Prove(
                owner.OwnerSubject, ticket, signature, DateTimeOffset.UtcNow);
            return success
                ? Results.Ok(new { possessionVerified = true, approved = false })
                : Results.BadRequest(new { error = "Proof rejected." });
        });
        group.MapPost("/approve", (HttpContext context, TicketRequest request) =>
        {
            if (!Auth0OwnerSignInProbe.IsValidatedOwner(context))
                return Results.Forbid();
            if (request.TicketId is not { Length: 48 } ticket ||
                !IsHex(ticket, 48) ||
                !TryOperatorSecret(context, out var secret))
                return Results.Forbid();

            // Never accept requested scopes from request body. This local
            // milestone grants only read-only conversation access.
            var approved = coordinator.ApproveAndPersist(
                owner.OwnerSubject, ticket, new[] { "conversation:read" },
                secret, DateTimeOffset.UtcNow);
            if (approved is null) return Results.Forbid();
            return Results.Ok(new
            {
                deviceId = approved.DeviceId,
                fingerprint = approved.PublicKeyFingerprint,
                scopes = new[] { "conversation:read" },
                enrolled = true, remoteAccessEnabled = false
            });
        });
        group.MapPost("/revoke", (HttpContext context, RevokeRequest request) =>
        {
            if (!Auth0OwnerSignInProbe.IsValidatedOwner(context))
                return Results.Forbid();
            if (request.DeviceId is not { Length: 48 } device ||
                !IsHex(device, 48) ||
                !TryOperatorSecret(context, out var secret))
                return Results.Forbid();
            var revoked = coordinator.Revoke(
                owner.OwnerSubject, device, secret, DateTimeOffset.UtcNow);
            return revoked
                ? Results.Ok(new { revoked = true })
                : Results.Forbid();
        });
    }

    private static bool TryOperatorSecret(HttpContext context, out string secret)
    {
        secret = "";
        if (!context.Request.Headers.TryGetValue(
                "X-Archer-Operator-Secret", out var values) ||
            values.Count != 1 || values[0] is not { Length: >= 48 and <= 256 } value ||
            value.Any(char.IsControl))
            return false;
        secret = value;
        return true;
    }

    internal static bool TryDecode(
        string? raw, int minBytes, int maxBytes, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (raw is null || raw.Length is < 1 or > 512 ||
            !Regex.IsMatch(raw, "^[A-Za-z0-9_-]+$",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return false;
        var normalized = raw.Replace('-', '+').Replace('_', '/');
        if (normalized.Length % 4 == 1) return false;
        normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
        try
        {
            bytes = Convert.FromBase64String(normalized);
            return bytes.Length >= minBytes && bytes.Length <= maxBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsHex(string value, int length) =>
        value.Length == length &&
        value.All(ch => ch is >= '0' and <= '9' or >= 'A' and <= 'F');
}
