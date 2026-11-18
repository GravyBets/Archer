using Archer.RemoteGateway;
using System.Security.Cryptography;

const string ownerA = "auth0|synthetic-owner-a";
const string ownerB = "auth0|synthetic-owner-b";
const string operatorSecret =
    "SYNTHETIC_LOCAL_ONLY_TEST_OPERATOR_SECRET_DO_NOT_USE_FOR_PRODUCTION_0118";
var now = new DateTimeOffset(2026, 11, 18, 14, 0, 0, TimeSpan.Zero);
var count = 0;
void Check(bool condition, string label)
{
    count++;
    if (!condition) throw new InvalidOperationException("FAILED: " + label);
}
using var correctKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var spki = correctKey.ExportSubjectPublicKeyInfo();
var folder = Path.Combine(
    Path.GetTempPath(), "archer-enrollment-pipeline-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "enrollment-fixture.sqlite");
    var registry = new DurableDeviceRegistry(path, createIfMissing: true);
    var coordinator = new OwnerEnrollmentCoordinator(registry, operatorSecret);

    var challenge = coordinator.Begin(ownerA, "Synthetic iPhone", spki, now);
    byte[] Sign(ECDsa key, string owner) =>
        key.SignData(DevicePairingContract.ProofPayload(owner, challenge),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    Check(registry.CountAudits(ownerA, new string('A', 48)) == 0,
        "begin cannot persist enrollment");
    Check(!coordinator.Prove(ownerB, challenge.TicketId, Sign(correctKey, ownerA), now),
        "same cryptographic proof rejected for different authenticated owner");
    Check(!coordinator.Prove(ownerA, challenge.TicketId, Sign(otherKey, ownerA), now),
        "wrong phone private key rejected");
    Check(coordinator.ApproveAndPersist(ownerA, challenge.TicketId,
        new[] { "conversation:read" }, operatorSecret, now) is null,
        "cannot approve without proof of possession");
    Check(coordinator.Prove(ownerA, challenge.TicketId, Sign(correctKey, ownerA), now),
        "correct owner and private key can prove once");
    Check(!coordinator.Prove(ownerA, challenge.TicketId, Sign(correctKey, ownerA), now),
        "cannot replay the same proof");
    Check(coordinator.ApproveAndPersist(ownerA, challenge.TicketId,
        new[] { "conversation:read" }, null, now) is null,
        "missing local operator approval credential denied");
    Check(coordinator.ApproveAndPersist(ownerA, challenge.TicketId,
        new[] { "conversation:read" }, new string('Z', 64), now) is null,
        "incorrect operator approval credential denied");
    Check(coordinator.ApproveAndPersist(ownerB, challenge.TicketId,
        new[] { "conversation:read" }, operatorSecret, now) is null,
        "wrong authenticated owner cannot approve another phone");
    Check(registry.CountAudits(ownerA, new string('A', 48)) == 0,
        "rejected approvals cannot store a grant");
    var enrolled = coordinator.ApproveAndPersist(ownerA, challenge.TicketId,
        new[] { "conversation:read" }, operatorSecret, now);
    Check(enrolled is not null && enrolled.DeviceId.Length == 48,
        "correct operator approval persists enrollment");
    Check(coordinator.ApproveAndPersist(ownerA, challenge.TicketId,
        new[] { "conversation:read" }, operatorSecret, now) is null,
        "approved ticket consumed exactly once");
    Check(coordinator.CurrentScopes(ownerA, enrolled!.DeviceId) is { Count: 1 },
        "only read-only scope granted");
    Check(coordinator.CurrentScopes(ownerB, enrolled.DeviceId) is null,
        "cross-owner lookup fails");
    Check(!coordinator.Revoke(ownerB, enrolled.DeviceId, operatorSecret, now),
        "cross-owner cannot revoke approved phone");
    Check(!coordinator.Revoke(ownerA, enrolled.DeviceId, new string('Z', 64), now),
        "operator approval required for revocation");
    Check(coordinator.CurrentScopes(ownerA, enrolled.DeviceId) is { Count: 1 },
        "invalid revocations do not remove grants");
    Check(coordinator.Revoke(ownerA, enrolled.DeviceId, operatorSecret, now),
        "owner with operator approval can revoke");
    Check(coordinator.CurrentScopes(ownerA, enrolled.DeviceId) is null,
        "revoked device immediately loses all scopes");
    Check(new DurableDeviceRegistry(path).CurrentScopes(ownerA, enrolled.DeviceId) is null,
        "revocation persists through registry restart");
    Check(registry.CountAudits(ownerA, enrolled.DeviceId) == 2,
        "exactly one enroll and one revoke audit rows");

    // A valid phone signing key is never authority by itself.
    var another = coordinator.Begin(ownerA, "Second synthetic iPhone", spki, now);
    var anotherSignature = correctKey.SignData(
        DevicePairingContract.ProofPayload(ownerA, another),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    Check(coordinator.Prove(ownerA, another.TicketId, anotherSignature, now),
        "fresh phone possession proof");
    Check(coordinator.ApproveAndPersist(ownerA, another.TicketId,
        new[] { "conversation:write" }, operatorSecret, now) is null,
        "write-only grants rejected even after proven key and operator approval");
    Check(coordinator.ApproveAndPersist(ownerA, another.TicketId,
        new[] { "admin:*" }, operatorSecret, now) is null,
        "no escalation to arbitrary admin scope");
    Check(coordinator.ApproveAndPersist(ownerA, another.TicketId,
        new[] { "conversation:read" }, operatorSecret,
        now.Add(DevicePairingContract.ChallengeLifetime)) is null,
        "approval after expiry is rejected");
    Console.WriteLine($"PASS: {count} end-to-end synthetic phone enrollment controls, " +
        "verified owner, separate operator credential, replay/expiry and durable revocation; no HTTP");
}
finally
{
    try { Directory.Delete(folder, recursive: true); }
    catch { /* temporary fixture cleanup only */ }
}
