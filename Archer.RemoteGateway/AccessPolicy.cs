namespace Archer.RemoteGateway;

/// <summary>
/// SYNTHETIC CONTRACT ONLY: a future trusted OIDC authenticator must construct
/// this identity after verifying issuer/audience/signature, expiry, device
/// enrollment and revocation. NEVER populate it from a request header/body.
/// This file is not wired into any HTTP endpoint.
/// </summary>
internal sealed record TrustedDeviceIdentity(
    string Subject,
    string DeviceId,
    DateTimeOffset ExpiresAtUtc,
    bool DeviceRevoked,
    IReadOnlySet<string> Scopes);

internal static class AccessPolicy
{
    internal static bool CanReadConversation(
        TrustedDeviceIdentity? identity, string ownerSubject, DateTimeOffset now) =>
        Allows(identity, ownerSubject, now, "conversation:read");

    // Writes also require read scope; a write-only device cannot be enrolled.
    internal static bool CanWriteConversation(
        TrustedDeviceIdentity? identity, string ownerSubject, DateTimeOffset now) =>
        Allows(identity, ownerSubject, now, "conversation:read") &&
        Allows(identity, ownerSubject, now, "conversation:write");

    private static bool Allows(
        TrustedDeviceIdentity? identity, string ownerSubject,
        DateTimeOffset now, string requiredScope) =>
        identity is { DeviceRevoked: false, Scopes: not null } &&
        !string.IsNullOrWhiteSpace(identity.Subject) &&
        !string.IsNullOrWhiteSpace(identity.DeviceId) &&
        !string.IsNullOrWhiteSpace(ownerSubject) &&
        identity.ExpiresAtUtc > now &&
        string.Equals(identity.Subject, ownerSubject, StringComparison.Ordinal) &&
        identity.Scopes.Contains(requiredScope);
}
