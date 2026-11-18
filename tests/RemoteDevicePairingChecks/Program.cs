using Archer.RemoteGateway;
using System.Security.Cryptography;

var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
const string ownerA = "owner-synthetic-A";
const string ownerB = "owner-synthetic-B";
var read = new[] { "conversation:read" };
var readWrite = new[] { "conversation:read", "conversation:write" };
var checks = 0;

void Check(bool ok, string scenario)
{
    checks++;
    if (!ok) throw new InvalidOperationException("FAIL: " + scenario);
}

void Throws<T>(Action action, string scenario) where T : Exception
{
    var threw = false;
    try { action(); } catch (T) { threw = true; }
    Check(threw, scenario);
}

byte[] Sign(ECDsa key, string owner, DevicePairingContract.PairingChallenge challenge) =>
    key.SignData(DevicePairingContract.ProofPayload(owner, challenge),
        HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

using var keyA = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var keyB = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
var pub = keyA.ExportSubjectPublicKeyInfo();
var contract = new DevicePairingContract();

var first = contract.Begin(ownerA, "Synthetic iPhone", pub, now);
Check(first.TicketId.Length == 48 && first.Nonce.Length == 64, "unpredictable ticket and challenge lengths");
Check(first.ExpiresAtUtc == now.AddMinutes(5), "five-minute challenge expiry");
Check(contract.ListOwnDevices(ownerA).Count == 0, "pairing request is not enrollment");
Check(contract.Approve(ownerA, first.TicketId, readWrite, now) is null,
    "approval denied before device proves key possession");
Check(!contract.Prove("not-a-ticket", Sign(keyA, ownerA, first), now),
    "unknown challenge denied");
Check(!contract.Prove(first.TicketId, Sign(keyB, ownerA, first), now),
    "signature from different device rejected");
Check(!contract.Prove(first.TicketId, Sign(keyA, ownerB, first), now),
    "signature bound to exact owner, not another subject");
Check(contract.Prove(first.TicketId, Sign(keyA, ownerA, first), now),
    "correct P-256 proof accepted");
Check(!contract.Prove(first.TicketId, Sign(keyA, ownerA, first), now),
    "key proof cannot be replayed");
Check(contract.Approve(ownerB, first.TicketId, readWrite, now) is null,
    "cross-owner approval refused without consuming ticket");
Check(contract.Approve(ownerA, first.TicketId, new[] { "admin:*" }, now) is null,
    "administrative wildcard scope refused");
Check(contract.Approve(ownerA, first.TicketId, new[] { "conversation:write" }, now) is null,
    "write permission requires read permission");
Check(contract.Approve(ownerA, first.TicketId, Array.Empty<string>(), now) is null,
    "empty scopes denied");
var enrolled = contract.Approve(ownerA, first.TicketId, readWrite, now);
Check(enrolled is not null, "proved + approved device is enrolled");
Check(enrolled!.DeviceId.Length == 48, "device ID is opaque and random");
Check(enrolled.PublicKeyFingerprint == Convert.ToHexString(SHA256.HashData(pub)),
    "public-key fingerprint matches proposed key");
Check(enrolled.Label == "Synthetic iPhone", "approved device label retained");
Check(contract.Approve(ownerA, first.TicketId, readWrite, now) is null,
    "enrollment approval is one-time");
Check(contract.ListOwnDevices(ownerB).Count == 0, "different owner cannot list device");
Check(contract.ListOwnDevices(ownerA).Count == 1, "owner lists exactly own device");
Check(contract.IsGranted(ownerA, enrolled.DeviceId, "conversation:read"), "owner read grant");
Check(contract.IsGranted(ownerA, enrolled.DeviceId, "conversation:write"), "owner write grant");
Check(!contract.IsGranted(ownerA, enrolled.DeviceId, "admin:*"), "not granted scope denied");
Check(!contract.IsGranted(ownerB, enrolled.DeviceId, "conversation:read"),
    "cross-owner access denied");
Check(!contract.Revoke(ownerB, enrolled.DeviceId), "cross-owner cannot revoke");
Check(contract.IsGranted(ownerA, enrolled.DeviceId, "conversation:read"),
    "cross-owner revocation attempt has no effect");

// Returned grant set is a detached snapshot, not mutable internal state.
((HashSet<string>)enrolled.Scopes).Add("admin:*");
Check(!contract.IsGranted(ownerA, enrolled.DeviceId, "admin:*"),
    "caller cannot mutate stored scopes via response object");
Check(contract.Revoke(ownerA, enrolled.DeviceId), "owner can revoke an enrolled device");
Check(!contract.Revoke(ownerA, enrolled.DeviceId), "revocation idempotently rejects repeats");
Check(!contract.IsGranted(ownerA, enrolled.DeviceId, "conversation:read"),
    "revocation immediately denies previously granted read scope");
Check(!contract.IsGranted(ownerA, enrolled.DeviceId, "conversation:write"),
    "revocation immediately denies previously granted write scope");
Check(contract.ListOwnDevices(ownerA)[0].Revoked,
    "owner can inspect revoked synthetic metadata");

// A third failed proof burns the ticket, including any later correct proof.
var wrong = contract.Begin(ownerA, "Failed proof", pub, now);
for (var i = 0; i < 3; i++)
    Check(!contract.Prove(wrong.TicketId, Sign(keyB, ownerA, wrong), now),
        "incorrect proof denied at attempt " + (i + 1));
Check(!contract.Prove(wrong.TicketId, Sign(keyA, ownerA, wrong), now),
    "ticket killed after three invalid signatures");
Check(contract.Approve(ownerA, wrong.TicketId, read, now) is null,
    "burned ticket cannot be approved");

// Incorrect signature lengths also count against the three-attempt cap.
var malformed = contract.Begin(ownerA, "Malformed signatures", pub, now);
for (var i = 0; i < 3; i++)
    Check(!contract.Prove(malformed.TicketId, new byte[] { 0x01 }, now),
        "short signature denied and counted " + (i + 1));
Check(!contract.Prove(malformed.TicketId, Sign(keyA, ownerA, malformed), now),
    "malformed proof flood cannot bypass attempt cap");

// Expiry is enforced for both proof and approval.
var timedOut = contract.Begin(ownerA, "Expired", pub, now);
Check(!contract.Prove(timedOut.TicketId, Sign(keyA, ownerA, timedOut),
        now.Add(DevicePairingContract.ChallengeLifetime)),
    "proof fails exactly at expiry");
var approvedTooLate = contract.Begin(ownerA, "Proved then expired", pub, now);
Check(contract.Prove(approvedTooLate.TicketId,
        Sign(keyA, ownerA, approvedTooLate), now), "proof before expiry works");
Check(contract.Approve(ownerA, approvedTooLate.TicketId, read,
        now.Add(DevicePairingContract.ChallengeLifetime)) is null,
    "approved after expiry is denied");

// Invalid identities, labels, curves and public key corruption fail before enrollment.
Throws<ArgumentException>(() => contract.Begin("", "Phone", pub, now), "blank owner denied");
Throws<ArgumentException>(() => contract.Begin(ownerA, "\n", pub, now), "control label denied");
Throws<ArgumentException>(() => contract.Begin(ownerA, new string('X', 81), pub, now),
    "oversized label denied");
Throws<ArgumentException>(() => contract.Begin(ownerA, "Phone", Array.Empty<byte>(), now),
    "missing device key denied");
Throws<ArgumentException>(() => contract.Begin(ownerA, "Phone",
    wrongCurve.ExportSubjectPublicKeyInfo(), now), "non-P256 curve denied");
Throws<ArgumentException>(() => contract.Begin(ownerA, "Phone",
    pub.Concat(new byte[] { 0 }).ToArray(), now), "trailing data in key denied");
Throws<ArgumentException>(() => contract.Begin(ownerA, "Phone",
    RandomNumberGenerator.GetBytes(100), now), "malformed key denied");

// Capacity is eight active devices per owner. Revoked devices free a slot.
var active = new List<string>();
for (var i = 0; i < DevicePairingContract.MaxActiveDevices; i++)
{
    var challenge = contract.Begin(ownerA, "Phone " + i, pub, now);
    Check(contract.Prove(challenge.TicketId, Sign(keyA, ownerA, challenge), now),
        "device " + i + " proved");
    var item = contract.Approve(ownerA, challenge.TicketId, read, now);
    Check(item is not null, "device " + i + " approved");
    active.Add(item!.DeviceId);
}
var extra = contract.Begin(ownerA, "Phone beyond cap", pub, now);
Check(contract.Prove(extra.TicketId, Sign(keyA, ownerA, extra), now),
    "extra device proof allowed but not enrolled");
Check(contract.Approve(ownerA, extra.TicketId, read, now) is null,
    "nine simultaneous active devices denied");
Check(contract.Revoke(ownerA, active[0]), "owner revokes to free a slot");
Check(contract.Approve(ownerA, extra.TicketId, read, now) is not null,
    "free slot permits approved new device");

// Concurrent owner approvals must consume the same ticket exactly once.
var parallelRegistry = new DevicePairingContract();
var parallel = parallelRegistry.Begin(ownerA, "Concurrent phone", pub, now);
Check(parallelRegistry.Prove(parallel.TicketId, Sign(keyA, ownerA, parallel), now),
    "concurrent ticket proved");
var accepted = 0;
Parallel.For(0, 32, _ =>
{
    if (parallelRegistry.Approve(ownerA, parallel.TicketId, readWrite, now) is not null)
        Interlocked.Increment(ref accepted);
});
Check(accepted == 1, "one and only one enrollment succeeds under concurrency");
Check(parallelRegistry.ListOwnDevices(ownerA).Count == 1,
    "no duplicate devices generated from concurrent approvals");

// Bounded challenge allocation; removing expired tickets recovers quota.
var bounded = new DevicePairingContract();
var ids = new HashSet<string>(StringComparer.Ordinal);
for (var i = 0; i < DevicePairingContract.MaxPendingChallenges; i++)
    ids.Add(bounded.Begin(ownerA, "Phone", pub, now).TicketId);
Check(ids.Count == DevicePairingContract.MaxPendingChallenges,
    "all random ticket IDs unique");
Throws<InvalidOperationException>(() => bounded.Begin(ownerA, "Phone", pub, now),
    "33rd pending challenge denied");
Check(bounded.Begin(ownerA, "After expiry", pub,
    now.Add(DevicePairingContract.ChallengeLifetime)).TicketId.Length == 48,
    "expired pairing tickets are swept to restore bounded capacity");

Console.WriteLine($"PASS: {checks} synthetic P-256 device pairing, approval, ownership, "
    + "scope, expiry, replay, concurrency, capacity and revocation checks. No network endpoint.");
