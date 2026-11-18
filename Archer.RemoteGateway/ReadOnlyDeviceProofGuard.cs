using System.Security.Cryptography;
using System.Text;

namespace Archer.RemoteGateway;

/// <summary>
/// LOCAL-NETWORK-DISABLED CONTRACT. Tests possession of the enrolled iPhone
/// key for a specific owner and one read-only resource. No HTTP endpoints,
/// no conversation data, no token issuance and no production claim.
///
/// A future endpoint must derive owner and requested path from verified
/// server-side identity/router state (never a phone-supplied owner or path),
/// validate TLS and JWT, and invoke VerifyAndConsume immediately before
/// authorizing that one request. In-memory challenges fail closed on restart.
/// </summary>
internal sealed class ReadOnlyDeviceProofGuard
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(90);
    internal const int MaxPending = 32;
    internal const int MaxFailures = 3;
    private const string Domain = "ARCHER_DEVICE_READ_PROOF_V1";

    private static readonly Dictionary<string, string> AllowedOperations =
        new(StringComparer.Ordinal)
        {
            ["/v1/conversation/messages"] = "conversation:read",
            ["/v1/inbox"] = "notifications:receive"
        };

    private readonly DurableDeviceRegistry _registry;
    private readonly object _lock = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    internal sealed record Challenge(
        string TicketId, string Nonce, DateTimeOffset ExpiresAtUtc);

    private sealed class Pending
    {
        internal required string Owner { get; init; }
        internal required string DeviceId { get; init; }
        internal required string Path { get; init; }
        internal required Challenge Challenge { get; init; }
        internal int Failures;
    }

    internal ReadOnlyDeviceProofGuard(DurableDeviceRegistry registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    internal Challenge? Begin(
        string validatedOwner, string deviceId, string method,
        string requestPath, DateTimeOffset serverNow)
    {
        if (!IsReadOperation(method, requestPath) ||
            !ValidOwner(validatedOwner) || !ValidDeviceId(deviceId))
            return null;
        var scope = AllowedOperations[requestPath];
        // Read from durable store for every challenge; neither a signed JWT
        // nor a stale cached enrollment can override a device revocation.
        if (_registry.CurrentScopes(validatedOwner, deviceId) is not { } grants ||
            !grants.Contains(scope) ||
            _registry.CurrentActiveKeyFingerprint(validatedOwner, deviceId) is null)
            return null;

        lock (_lock)
        {
            Sweep(serverNow);
            if (_pending.Count >= MaxPending) return null;
            var challenge = new Challenge(
                Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                serverNow.Add(Lifetime));
            _pending.Add(challenge.TicketId, new Pending
            {
                Owner = validatedOwner,
                DeviceId = deviceId,
                Path = requestPath,
                Challenge = challenge
            });
            return challenge;
        }
    }

    internal static byte[] ProofPayload(
        string validatedOwner, string deviceId, string requestPath,
        Challenge challenge)
    {
        if (!ValidOwner(validatedOwner) || !ValidDeviceId(deviceId) ||
            !AllowedOperations.ContainsKey(requestPath) ||
            !IsHex(challenge.TicketId, 48) || !IsHex(challenge.Nonce, 64))
            throw new ArgumentException("Invalid signed read request context.");
        return Encoding.UTF8.GetBytes(
            Domain + "\n" + validatedOwner + "\n" + deviceId + "\n" +
            "GET" + "\n" + requestPath + "\n" +
            challenge.TicketId + "\n" + challenge.Nonce);
    }

    internal bool VerifyAndConsume(
        string validatedOwner, string deviceId, string method,
        string requestPath, string ticketId, byte[] publicSpki,
        byte[] rawP256Signature, DateTimeOffset serverNow)
    {
        if (!IsReadOperation(method, requestPath) ||
            !ValidOwner(validatedOwner) || !ValidDeviceId(deviceId) ||
            string.IsNullOrEmpty(ticketId)) return false;

        lock (_lock)
        {
            if (!_pending.TryGetValue(ticketId, out var pending) ||
                pending.Challenge.ExpiresAtUtc <= serverNow ||
                !string.Equals(pending.Owner, validatedOwner, StringComparison.Ordinal) ||
                !string.Equals(pending.DeviceId, deviceId, StringComparison.Ordinal) ||
                !string.Equals(pending.Path, requestPath, StringComparison.Ordinal))
                return false;

            // Re-check durable revocation at PROOF time. The token alone never
            // authorizes, and the public SPKI must match the enrollment key.
            var scope = AllowedOperations[requestPath];
            var grants = _registry.CurrentScopes(validatedOwner, deviceId);
            var fingerprint = _registry.CurrentActiveKeyFingerprint(
                validatedOwner, deviceId);
            if (grants is null || !grants.Contains(scope) ||
                fingerprint is null)
            {
                _pending.Remove(ticketId);
                return false;
            }

            var verified = false;
            if (publicSpki is { Length: >= 64 and <= 256 } &&
                rawP256Signature is { Length: 64 })
            {
                try
                {
                    var expected = Convert.FromHexString(fingerprint);
                    var observed = SHA256.HashData(publicSpki);
                    if (CryptographicOperations.FixedTimeEquals(expected, observed))
                    {
                        using var key = ECDsa.Create();
                        key.ImportSubjectPublicKeyInfo(publicSpki, out var length);
                        verified = length == publicSpki.Length &&
                            key.KeySize == 256 &&
                            key.ExportParameters(false).Curve.Oid.Value ==
                                "1.2.840.10045.3.1.7" &&
                            key.VerifyData(
                                ProofPayload(validatedOwner, deviceId, requestPath,
                                    pending.Challenge),
                                rawP256Signature, HashAlgorithmName.SHA256,
                                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                    }
                }
                catch (CryptographicException)
                {
                    verified = false;
                }
            }

            if (verified)
            {
                _pending.Remove(ticketId);
                return true; // Only the specific, validated GET may proceed.
            }

            pending.Failures++;
            if (pending.Failures >= MaxFailures)
                _pending.Remove(ticketId);
            return false;
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var id in _pending.Values
            .Where(p => p.Challenge.ExpiresAtUtc <= now)
            .Select(p => p.Challenge.TicketId).ToArray())
            _pending.Remove(id);
    }

    private static bool IsReadOperation(string? method, string? path) =>
        string.Equals(method, "GET", StringComparison.Ordinal) &&
        path is not null && AllowedOperations.ContainsKey(path);

    private static bool ValidOwner(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        !value.Any(char.IsControl);

    private static bool ValidDeviceId(string? value) => IsHex(value, 48);

    private static bool IsHex(string? value, int length) =>
        value is not null && value.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
