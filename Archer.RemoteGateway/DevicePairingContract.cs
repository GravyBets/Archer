using System.Security.Cryptography;
using System.Text;

namespace Archer.RemoteGateway;

/// <summary>
/// Non-networked, volatile, synthetic prototype of owner-approved P-256 device
/// enrollment. It is deliberately NOT registered in DI or an HTTP endpoint.
/// An authenticated owner session, real persistence, device recovery, audit,
/// replay-resistant per-request proof and distributed limits are still required.
/// Caller-provided timestamps here are TEST CLOCKS, never client-supplied time.
/// </summary>
internal sealed class DevicePairingContract
{
    internal const int MaxActiveDevices = 8;
    internal const int MaxPendingChallenges = 32;
    internal const int MaxFailedProofs = 3;
    internal static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private const string P256Oid = "1.2.840.10045.3.1.7";
    private const string Domain = "ARCHER_DEVICE_PAIR_P256_V1";
    private static readonly HashSet<string> AllowedScopes = new(StringComparer.Ordinal)
    {
        "conversation:read",
        "conversation:write",
        "notifications:receive",
        "receipts:write"
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, PendingPair> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnrolledDevice> _devices = new(StringComparer.Ordinal);

    internal sealed record PairingChallenge(
        string TicketId, string Nonce, DateTimeOffset ExpiresAtUtc);

    internal sealed record DeviceSummary(
        string DeviceId, string OwnerSubject, string Label,
        string PublicKeyFingerprint, IReadOnlySet<string> Scopes,
        bool Revoked, DateTimeOffset EnrolledAtUtc);

    private sealed class PendingPair
    {
        internal required string OwnerSubject { get; init; }
        internal required string Label { get; init; }
        internal required byte[] PublicKeySpki { get; init; }
        internal required PairingChallenge Challenge { get; init; }
        internal int FailedProofs { get; set; }
        internal bool Proved { get; set; }
    }

    private sealed class EnrolledDevice
    {
        internal required string DeviceId { get; init; }
        internal required string OwnerSubject { get; init; }
        internal required string Label { get; init; }
        internal required string PublicKeyFingerprint { get; init; }
        internal required byte[] PublicKeySpki { get; init; }
        internal required IReadOnlySet<string> Scopes { get; init; }
        internal required DateTimeOffset EnrolledAtUtc { get; init; }
        internal bool Revoked { get; set; }

        internal DeviceSummary Snapshot() => new(
            DeviceId, OwnerSubject, Label, PublicKeyFingerprint,
            new HashSet<string>(Scopes, StringComparer.Ordinal),
            Revoked, EnrolledAtUtc);
    }

    /// <summary>
    /// Begin a pairing intent from an already-authenticated source. Does not
    /// approve a device. Only an owner-approved and device-proved ticket
    /// can become an enrollment.
    /// </summary>
    internal PairingChallenge Begin(
        string ownerSubject, string deviceLabel, byte[] publicKeySpki,
        DateTimeOffset now)
    {
        ValidateText(ownerSubject, 128, "owner subject");
        ValidateText(deviceLabel, 80, "device label");
        ValidateP256PublicKey(publicKeySpki);

        lock (_gate)
        {
            RemoveExpiredPending(now);
            if (_pending.Count >= MaxPendingChallenges)
                throw new InvalidOperationException("Too many outstanding synthetic pairing requests.");

            var challenge = new PairingChallenge(
                Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                now.Add(ChallengeLifetime));

            _pending.Add(challenge.TicketId, new PendingPair
            {
                OwnerSubject = ownerSubject,
                Label = deviceLabel,
                PublicKeySpki = (byte[])publicKeySpki.Clone(),
                Challenge = challenge
            });
            return challenge;
        }
    }

    /// <summary>
    /// Data signed with the proposed device's private Secure Enclave key.
    /// The domain, random nonce, ticket and owner all bind the signature.
    /// </summary>
    internal static byte[] ProofPayload(
        string ownerSubject, PairingChallenge challenge) =>
        Encoding.UTF8.GetBytes(
            $"{Domain}\n{ownerSubject}\n{challenge.TicketId}\n{challenge.Nonce}");

    /// <summary>
    /// Verify possession of the exact public key submitted at Begin.
    /// Failed attempts are limited and a proved ticket is one-time.
    /// </summary>
    internal bool Prove(string ticketId, byte[] signature, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(ticketId) || signature is not { Length: 64 })
            return false;
        lock (_gate)
        {
            if (!_pending.TryGetValue(ticketId, out var pending) ||
                now >= pending.Challenge.ExpiresAtUtc || pending.Proved)
                return false;

            bool valid;
            using (var verifier = ECDsa.Create())
            {
                verifier.ImportSubjectPublicKeyInfo(pending.PublicKeySpki, out _);
                valid = verifier.VerifyData(
                    ProofPayload(pending.OwnerSubject, pending.Challenge),
                    signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            if (valid)
            {
                pending.Proved = true;
                return true;
            }

            pending.FailedProofs++;
            if (pending.FailedProofs >= MaxFailedProofs)
                _pending.Remove(ticketId);
            return false;
        }
    }

    /// <summary>
    /// This owner string MUST come from a trusted server-side authentication
    /// context when the contract is implemented. Never call this from a
    /// request with a user-asserted owner string.
    /// </summary>
    internal DeviceSummary? Approve(
        string trustedOwnerSubject, string ticketId,
        IReadOnlyCollection<string> grantedScopes, DateTimeOffset now)
    {
        ValidateText(trustedOwnerSubject, 128, "owner subject");
        if (string.IsNullOrWhiteSpace(ticketId) || grantedScopes is null ||
            grantedScopes.Count == 0 || grantedScopes.Count > AllowedScopes.Count ||
            grantedScopes.Any(scope => !AllowedScopes.Contains(scope)) ||
            (grantedScopes.Contains("conversation:write") &&
             !grantedScopes.Contains("conversation:read")))
            return null;

        lock (_gate)
        {
            if (!_pending.TryGetValue(ticketId, out var pending) ||
                now >= pending.Challenge.ExpiresAtUtc || !pending.Proved ||
                !string.Equals(pending.OwnerSubject, trustedOwnerSubject,
                    StringComparison.Ordinal))
                return null;

            if (_devices.Values.Count(d =>
                d.OwnerSubject == trustedOwnerSubject && !d.Revoked) >= MaxActiveDevices)
                return null;

            var device = new EnrolledDevice
            {
                DeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
                OwnerSubject = trustedOwnerSubject,
                Label = pending.Label,
                PublicKeyFingerprint = Convert.ToHexString(
                    SHA256.HashData(pending.PublicKeySpki)),
                PublicKeySpki = (byte[])pending.PublicKeySpki.Clone(),
                Scopes = new HashSet<string>(grantedScopes, StringComparer.Ordinal),
                EnrolledAtUtc = now
            };
            _devices.Add(device.DeviceId, device);
            _pending.Remove(ticketId); // Cannot be approved a second time.
            return device.Snapshot();
        }
    }

    internal bool IsGranted(string authenticatedOwnerSubject, string deviceId, string scope)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(deviceId, out var device) &&
                !device.Revoked &&
                string.Equals(device.OwnerSubject, authenticatedOwnerSubject,
                    StringComparison.Ordinal) &&
                device.Scopes.Contains(scope);
        }
    }

    /// <summary>
    /// Immediate in-memory deny for this prototype. Production must check
    /// durable revocation on every request, including valid existing JWTs.
    /// </summary>
    internal bool Revoke(string trustedOwnerSubject, string deviceId)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var device) ||
                device.Revoked ||
                !string.Equals(device.OwnerSubject, trustedOwnerSubject,
                    StringComparison.Ordinal))
                return false;

            device.Revoked = true;
            return true;
        }
    }

    internal IReadOnlyList<DeviceSummary> ListOwnDevices(string trustedOwnerSubject)
    {
        lock (_gate)
        {
            return _devices.Values
                .Where(d => string.Equals(d.OwnerSubject, trustedOwnerSubject,
                    StringComparison.Ordinal))
                .OrderBy(d => d.EnrolledAtUtc).ThenBy(d => d.DeviceId)
                .Select(d => d.Snapshot())
                .ToArray();
        }
    }

    private void RemoveExpiredPending(DateTimeOffset now)
    {
        var expiredIds = _pending.Values
            .Where(p => now >= p.Challenge.ExpiresAtUtc)
            .Select(p => p.Challenge.TicketId)
            .ToArray();
        foreach (var id in expiredIds)
            _pending.Remove(id);
    }

    private static void ValidateText(string value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException("Invalid synthetic " + field);
    }

    private static void ValidateP256PublicKey(byte[] spki)
    {
        if (spki is null || spki.Length is < 64 or > 256)
            throw new ArgumentException("Invalid synthetic device public key.");

        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length || verifier.KeySize != 256 ||
                verifier.ExportParameters(false).Curve.Oid.Value != P256Oid)
                throw new ArgumentException("Only complete P-256 public keys are permitted.");
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException("Invalid synthetic P-256 public key.", ex);
        }
    }
}
