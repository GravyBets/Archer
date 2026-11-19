using System.Security.Cryptography;
using System.Text;

namespace Archer.RemoteGateway;

/// <summary>
/// Owner-signed and phone-key-signed local read-only mailbox. This is not
/// a remote service or a model/chat transport. The only writer is a separate
/// explicitly authorized LOCAL operator action, never a phone or LLM.
/// </summary>
internal static class LocalMailboxRoutes
{
    internal sealed record DeviceChallengeRequest(string? DeviceId);
    internal sealed record EnqueueRequest(string? Content);

    internal static void Map(
        WebApplication app, Auth0OwnerSignInSettings owner,
        LocalPairingSettings pairing, LocalMailboxSettings mailbox)
    {
        var registry = new DurableDeviceRegistry(pairing.RegistryPath);
        var guard = new ReadOnlyDeviceProofGuard(registry);
        var journal = new EncryptedRemoteMailbox(mailbox);

        var group = app.MapGroup("/_local/mailbox").RequireAuthorization();
        group.MapPost("/challenge", (
            HttpContext context, DeviceChallengeRequest request) =>
        {
            if (!Auth0OwnerSignInProbe.IsValidatedOwner(context))
                return Results.Forbid();
            if (request.DeviceId is not { Length: 48 } deviceId)
                return Results.BadRequest(new { error = "Invalid device identifier." });
            var challenge = guard.Begin(
                owner.OwnerSubject, deviceId, "GET", "/v1/conversation/messages",
                DateTimeOffset.UtcNow);
            if (challenge is null) return Results.Forbid();
            return Results.Ok(new
            {
                ownerSubject = owner.OwnerSubject,
                deviceId, requestMethod = "GET",
                requestPath = "/v1/conversation/messages",
                ticketId = challenge.TicketId, nonce = challenge.Nonce,
                expiresAtUtc = challenge.ExpiresAtUtc,
                remoteAccessEnabled = false
            });
        });

        // Operator-originated synthetic/local message only. The phone has
        // NO write endpoint; AI and desktop conversations are not connected.
        group.MapPost("/enqueue", (
            HttpContext context, EnqueueRequest request) =>
        {
            if (!Auth0OwnerSignInProbe.IsValidatedOwner(context) ||
                !AuthorizedOperator(context, pairing.OperatorSecret))
                return Results.Forbid();
            if (request.Content is not { Length: >= 1 and <= 1200 } content)
                return Results.BadRequest(new { error = "Invalid message length." });
            try
            {
                var added = journal.Enqueue(
                    owner.OwnerSubject, content, DateTimeOffset.UtcNow);
                return Results.Ok(new
                {
                    id = added.Id, sequence = added.Sequence,
                    storedOnly = true, remoteDeliveryEnabled = false,
                    aiConnected = false
                });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "Invalid bounded message." });
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
        });

        // GET only. A valid signed Auth0 login is necessary but insufficient:
        // a one-time device signature over exact owner/device/path is needed.
        app.MapGet("/v1/conversation/messages", (HttpContext context) =>
        {
            if (!Auth0OwnerSignInProbe.IsValidatedOwner(context))
                return Results.Forbid();
            if (context.Request.Query.Count > 2 ||
                context.Request.Query.Keys.Any(k => k is not ("after" or "limit")))
                return Results.BadRequest(new { error = "Unsupported page request." });
            long after = 0;
            int limit = 30;
            if (context.Request.Query.TryGetValue("after", out var afterRaw) &&
                (afterRaw.Count != 1 || !long.TryParse(afterRaw[0], out after) ||
                 after < 0))
                return Results.BadRequest(new { error = "Invalid after cursor." });
            if (context.Request.Query.TryGetValue("limit", out var limitRaw) &&
                (limitRaw.Count != 1 || !int.TryParse(limitRaw[0], out limit) ||
                 limit is < 1 or > 50))
                return Results.BadRequest(new { error = "Invalid page size." });

            var device = Single(context, "X-Archer-Device-Id");
            var ticket = Single(context, "X-Archer-Device-Ticket");
            var keyRaw = Single(context, "X-Archer-Public-Key");
            var proofRaw = Single(context, "X-Archer-Device-Proof");
            if (device is null || ticket is null ||
                !LocalPairingRoutes.TryDecode(keyRaw, 64, 256, out var publicSpki) ||
                !LocalPairingRoutes.TryDecode(proofRaw, 64, 64, out var proof))
                return Results.Forbid();

            var now = DateTimeOffset.UtcNow;
            if (!guard.VerifyAndConsume(
                    owner.OwnerSubject, device, "GET", "/v1/conversation/messages",
                    ticket, publicSpki, proof, now))
                return Results.Forbid();
            // Re-check the durable grant on data read as well. Tokens/earlier
            // challenges do not override a revoked enrollment.
            if (registry.CurrentScopes(owner.OwnerSubject, device)
                is not { } scopes || !scopes.Contains("conversation:read"))
                return Results.Forbid();
            try
            {
                var messages = journal.Read(owner.OwnerSubject, after, limit, now);
                return Results.Ok(new
                {
                    messages,
                    nextAfter = messages.Count == 0 ? after : messages[^1].Sequence,
                    count = messages.Count,
                    savedOnly = true,
                    aiConnected = false,
                    phoneWriteEnabled = false,
                    remoteAccessEnabled = false
                });
            }
            catch (CryptographicException)
            {
                // Never fall back to plaintext or emit corrupted content.
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization();
    }

    private static string? Single(HttpContext context, string name) =>
        context.Request.Headers.TryGetValue(name, out var values) &&
        values.Count == 1 && values[0] is { Length: > 0 and <= 512 } value
            ? value : null;

    private static bool AuthorizedOperator(
        HttpContext context, string expectedSecret)
    {
        var received = Single(context, "X-Archer-Operator-Secret");
        if (received is null ||
            received.Length != expectedSecret.Length ||
            received.Length is < 48 or > 256) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(received),
            Encoding.UTF8.GetBytes(expectedSecret));
    }
}
