using System.Security.Cryptography;
using Archer.RemoteGateway;

const string owner = "auth0|synthetic_owner";
const string adversary = "auth0|wrong_owner";
const string path = "/v1/conversation/messages";
var now = new DateTimeOffset(2026, 11, 18, 16, 0, 0, TimeSpan.Zero);
var checks = 0;
void Check(bool accepted, string scenario)
{
    checks++;
    if (!accepted) throw new Exception("FAIL: " + scenario);
}
var dir = Path.Combine(Path.GetTempPath(),
    "archer-read-proof-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var registry = new DurableDeviceRegistry(
        Path.Combine(dir, "test.sqlite"), createIfMissing: true);
    using var phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using var rogueKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var spki = phoneKey.ExportSubjectPublicKeyInfo();
    var pairing = new DevicePairingContract();
    var guard = new ReadOnlyDeviceProofGuard(registry);
    Check(guard.Begin(owner, new string('A', 48), "GET", path, now) is null,
        "unenrolled device cannot request signing challenge");

    var original = pairing.Begin(owner, "Synthetic enrolled phone", spki, now);
    var enrollmentSignature = phoneKey.SignData(
        DevicePairingContract.ProofPayload(owner, original),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    Check(pairing.Prove(owner, original.TicketId, enrollmentSignature, now),
        "synthetic phone proves pairing key possession");
    var enrolled = pairing.Approve(owner, original.TicketId,
        new[] { "conversation:read" }, now)!;
    registry.SaveApproved(enrolled, now);
    var deviceId = enrolled.DeviceId;
    Check(registry.CurrentActiveKeyFingerprint(owner, deviceId) ==
          Convert.ToHexString(SHA256.HashData(spki)),
        "pinned fingerprint retrieved only for active device");
    Check(registry.CurrentActiveKeyFingerprint(adversary, deviceId) is null,
        "cross-owner cannot look up public key fingerprint");
    Check(guard.Begin(owner, deviceId, "POST", path, now) is null,
        "read-only proof cannot authorize POST");
    Check(guard.Begin(owner, deviceId, "GET", "/v1/missions/run", now) is null,
        "mission execution is not an allowed operation");
    Check(guard.Begin(owner, deviceId, "GET", path + "?admin=true", now) is null,
        "path variants are never inferred to be authorized");
    Check(guard.Begin(adversary, deviceId, "GET", path, now) is null,
        "wrong signed owner cannot get a phone challenge");
    Check(guard.Begin(owner, deviceId, "GET", "/v1/inbox", now) is null,
        "ungranted notification scope is rejected");

    var challenge = guard.Begin(owner, deviceId, "GET", path, now)!;
    byte[] Sign(ECDsa key, string ownerArg, string routeArg,
        ReadOnlyDeviceProofGuard.Challenge req) =>
        key.SignData(ReadOnlyDeviceProofGuard.ProofPayload(
            ownerArg, deviceId, routeArg, req),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    Check(challenge.TicketId.Length == 48 && challenge.Nonce.Length == 64,
        "unpredictable one-use challenge has full nonce entropy");
    Check(!guard.VerifyAndConsume(adversary, deviceId, "GET", path,
        challenge.TicketId, spki, Sign(phoneKey, owner, path, challenge), now),
        "wrong authenticated owner cannot consume valid phone proof");
    Check(!guard.VerifyAndConsume(owner, deviceId, "GET", "/v1/inbox",
        challenge.TicketId, spki, Sign(phoneKey, owner, path, challenge), now),
        "changing resource path invalidates the request proof");
    Check(!guard.VerifyAndConsume(owner, deviceId, "POST", path,
        challenge.TicketId, spki, Sign(phoneKey, owner, path, challenge), now),
        "changing HTTP method invalidates proof");
    Check(!guard.VerifyAndConsume(owner, deviceId, "GET", path,
        challenge.TicketId, rogueKey.ExportSubjectPublicKeyInfo(),
        Sign(rogueKey, owner, path, challenge), now),
        "a different public key does not match the enrolled fingerprint");
    Check(!guard.VerifyAndConsume(owner, deviceId, "GET", path,
        challenge.TicketId, spki, Sign(rogueKey, owner, path, challenge), now),
        "a forged signature with correct public key is rejected");
    Check(guard.VerifyAndConsume(owner, deviceId, "GET", path,
        challenge.TicketId, spki, Sign(phoneKey, owner, path, challenge), now),
        "authenticated owner and enrolled key can sign one exact GET");
    Check(!guard.VerifyAndConsume(owner, deviceId, "GET", path,
        challenge.TicketId, spki, Sign(phoneKey, owner, path, challenge), now),
        "identical cryptographic proof cannot be replayed");

    var expired = guard.Begin(owner, deviceId, "GET", path, now)!;
    Check(!guard.VerifyAndConsume(owner, deviceId, "GET", path,
        expired.TicketId, spki, Sign(phoneKey, owner, path, expired),
        now.Add(ReadOnlyDeviceProofGuard.Lifetime)),
        "a challenge is invalid at the exact expiration boundary");

    var burned = guard.Begin(owner, deviceId, "GET", path, now)!;
    for (var i = 0; i < ReadOnlyDeviceProofGuard.MaxFailures; i++)
        Check(!guard.VerifyAndConsume(owner, deviceId, "GET", path,
            burned.TicketId, spki, new byte[1], now),
            "malformed signatures consume failure allowance");
    Check(!guard.VerifyAndConsume(owner, deviceId, "GET", path,
        burned.TicketId, spki, Sign(phoneKey, owner, path, burned), now),
        "valid proof cannot resurrect burned challenge");

    var pending = guard.Begin(owner, deviceId, "GET", path, now)!;
    Check(registry.Revoke(owner, deviceId, now), "device can be revoked");
    Check(registry.CurrentActiveKeyFingerprint(owner, deviceId) is null,
        "revoked public key becomes inaccessible");
    Check(!guard.VerifyAndConsume(owner, deviceId, "GET", path,
        pending.TicketId, spki, Sign(phoneKey, owner, path, pending), now),
        "revocation denies a previously issued outstanding challenge");
    Check(guard.Begin(owner, deviceId, "GET", path, now) is null,
        "revoked phone cannot receive new challenges");
    Check(new ReadOnlyDeviceProofGuard(new DurableDeviceRegistry(
        Path.Combine(dir, "test.sqlite"))).Begin(owner, deviceId, "GET", path, now) is null,
        "revoked phone remains unauthorized after process restart");
    Console.WriteLine($"PASS: {checks} local-only owner/device read proof checks: " +
        "scope, fingerprint, replay, exact route, expiry and immediate revocation.");
}
finally
{
    try { Directory.Delete(dir, recursive: true); }
    catch { /* test-only cleanup */ }
}
