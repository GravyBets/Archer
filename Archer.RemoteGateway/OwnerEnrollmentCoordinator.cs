using System.Security.Cryptography;
using System.Text;

namespace Archer.RemoteGateway;

/// <summary>
/// LOCAL-ONLY integration boundary for device key proof, separate owner
/// approval, and durable revocation. No HTTP route calls this class yet.
///
/// trustedOwnerSubject must come from validated issuer/audience/owner claims
/// (NEVER from JSON). The operator approval secret is stored only in the
/// server process environment, never in the PWA or Git repository.
/// This is not production enrollment: pending tickets are memory-only,
/// public-key request signatures and multi-node replay controls are not wired.
/// </summary>
internal sealed class OwnerEnrollmentCoordinator
{
    private readonly DevicePairingContract _pairing = new();
    private readonly DurableDeviceRegistry _registry;
    private readonly byte[] _operatorSecretHash;

    internal OwnerEnrollmentCoordinator(
        DurableDeviceRegistry registry, string operatorSecret)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (string.IsNullOrWhiteSpace(operatorSecret) ||
            operatorSecret.Length < 48 || operatorSecret.Length > 256 ||
            operatorSecret.Any(char.IsControl))
            throw new ArgumentException(
                "Local operator approval must have a long, dedicated secret.");
        _operatorSecretHash = SHA256.HashData(Encoding.UTF8.GetBytes(operatorSecret));
    }

    internal DevicePairingContract.PairingChallenge Begin(
        string validatedOwnerSubject, string label, byte[] publicSpki,
        DateTimeOffset serverNow) =>
        _pairing.Begin(validatedOwnerSubject, label, publicSpki, serverNow);

    internal bool Prove(
        string validatedOwnerSubject, string ticketId, byte[] rawP256Signature,
        DateTimeOffset serverNow) =>
        _pairing.Prove(
            validatedOwnerSubject, ticketId, rawP256Signature, serverNow);

    /// <summary>
    /// Operator approval must be a deliberate, separately authenticated
    /// local action. Browser proof or a spoken YES is insufficient.
    /// Operator credential verification happens before any state change.
    /// Once approved, the grant is immediately recorded durably.
    /// The ticket remains used if storage fails; a fresh enrollment is safer
    /// than retrying a potentially partially completed approval.
    /// </summary>
    internal DevicePairingContract.DeviceSummary? ApproveAndPersist(
        string validatedOwnerSubject, string ticketId,
        IReadOnlyCollection<string> grantedScopes,
        string? suppliedOperatorSecret, DateTimeOffset serverNow)
    {
        if (string.IsNullOrEmpty(suppliedOperatorSecret) ||
            suppliedOperatorSecret.Length is < 48 or > 256)
            return null;

        var suppliedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedOperatorSecret));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    _operatorSecretHash, suppliedHash))
                return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(suppliedHash);
        }

        var approved = _pairing.Approve(
            validatedOwnerSubject, ticketId, grantedScopes, serverNow);
        if (approved is null) return null;

        _registry.SaveApproved(approved, serverNow);
        return approved;
    }

    internal bool Revoke(
        string validatedOwnerSubject, string deviceId,
        string? suppliedOperatorSecret, DateTimeOffset serverNow)
    {
        if (string.IsNullOrEmpty(suppliedOperatorSecret) ||
            suppliedOperatorSecret.Length is < 48 or > 256)
            return false;
        var suppliedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedOperatorSecret));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    _operatorSecretHash, suppliedHash))
                return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(suppliedHash);
        }
        return _registry.Revoke(
            validatedOwnerSubject, deviceId, serverNow);
    }

    // This is an entitlement lookup, not a full authenticated request.
    // Future endpoints must separately verify bearer token AND device-key
    // possession per request, including anti-replay and trusted TLS.
    internal IReadOnlySet<string>? CurrentScopes(
        string validatedOwnerSubject, string deviceId) =>
        _registry.CurrentScopes(validatedOwnerSubject, deviceId);
}
