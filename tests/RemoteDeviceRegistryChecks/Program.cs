using System.Security.Cryptography;
using Archer.RemoteGateway;
using Microsoft.Data.Sqlite;

const string ownerA = "owner-synthetic-A";
const string ownerB = "owner-synthetic-B";
var now = DateTimeOffset.UtcNow;
var assertions = 0;

void Check(bool pass, string scenario)
{
    assertions++;
    if (!pass)
        throw new InvalidOperationException("FAILED: " + scenario);
}

void Expect<T>(Action action, string scenario) where T : Exception
{
    var threw = false;
    try { action(); } catch (T) { threw = true; }
    Check(threw, scenario);
}

DevicePairingContract.DeviceSummary Approve(
    string owner, string label, IReadOnlyCollection<string> scopes)
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var inMemory = new DevicePairingContract();
    var challenge = inMemory.Begin(owner, label,
        key.ExportSubjectPublicKeyInfo(), now);
    var signature = key.SignData(
        DevicePairingContract.ProofPayload(owner, challenge),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    Check(inMemory.Prove(challenge.TicketId, signature, now),
        "device private key possession demonstrated offline");
    var approved = inMemory.Approve(owner, challenge.TicketId, scopes, now);
    Check(approved is not null, "trusted synthetic owner approved enrollment");
    return approved!;
}

if (args is ["seed", var path])
{
    var approved = Approve(ownerA, "CI synthetic iPhone",
        new[] { "conversation:read", "conversation:write" });
    var registry = new DurableDeviceRegistry(path, createIfMissing: true);
    registry.SaveApproved(approved, now);
    Check(registry.CurrentScopes(ownerA, approved.DeviceId) is not null,
        "seed grant stored");
    // Only opaque public synthetic device IDs are printed. No signing keys.
    Console.WriteLine("ENROLLED_DEVICE_ID=" + approved.DeviceId);
    return;
}

if (args is ["revoke", var path, var deviceId])
{
    var registry = new DurableDeviceRegistry(path);
    Check(registry.Revoke(ownerA, deviceId, now),
        "trusted synthetic owner revoked approved device");
    Console.WriteLine("REVOKED=true");
    return;
}

if (args.Length != 0)
    throw new ArgumentException("Usage: [seed ABSOLUTE_TEST_DB | revoke ABSOLUTE_TEST_DB DEVICE_ID]");

var folder = Path.Combine(
    Path.GetTempPath(), "archer-registry-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "synthetic-devices.sqlite");
    Expect<InvalidOperationException>(() => new DurableDeviceRegistry(path),
        "missing registry refuses to bootstrap automatically on access");
    var first = new DurableDeviceRegistry(path, createIfMissing: true);
    var full = Approve(ownerA, "Approved synthetic phone",
        new[] { "conversation:read", "conversation:write" });
    first.SaveApproved(full, now);
    Check(File.Exists(path), "SQLite registry was created");
    Check(first.CountAudits(ownerA, full.DeviceId) == 1,
        "enrollment recorded as an audit event");
    Check(first.CurrentScopes(ownerA, full.DeviceId) is { Count: 2 },
        "read and write grants available");
    Check(first.CurrentScopes(ownerB, full.DeviceId) is null,
        "second owner may not retrieve first owner's enrollment");
    Check(first.CurrentScopes(ownerA, "A" + full.DeviceId) is null,
        "malformed device ID denied");
    Check(first.CurrentScopes(ownerA, new string('A', 48)) is null,
        "unregistered device denied");

    var reopened = new DurableDeviceRegistry(path);
    Check(reopened.CurrentScopes(ownerA, full.DeviceId) is { Count: 2 },
        "device remains enrolled after SQLite connection is reopened");
    Expect<SqliteException>(() => reopened.SaveApproved(full, now),
        "same device ID cannot be enrolled twice");
    Check(reopened.CountAudits(ownerA, full.DeviceId) == 1,
        "duplicate enrollment cannot append an audit event");

    var restricted = Approve(ownerB, "Different owner",
        new[] { "conversation:read" });
    reopened.SaveApproved(restricted, now);
    Check(reopened.CurrentScopes(ownerB, restricted.DeviceId) is { Count: 1 },
        "another owner stores its own independent scope set");
    Check(reopened.CurrentScopes(ownerB, restricted.DeviceId)!.Contains(
        "conversation:read"), "read-only grant stored");
    Check(!reopened.CurrentScopes(ownerB, restricted.DeviceId)!.Contains(
        "conversation:write"), "storage never grants write from a read-only enrollment");
    Check(reopened.CurrentScopes(ownerA, restricted.DeviceId) is null,
        "owner A cannot inspect owner B's scopes");
    Check(reopened.CountAudits(ownerA, restricted.DeviceId) == 0,
        "one owner cannot see another owner's audit entries");

    var escalated = restricted with
    {
        DeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
        Scopes = new HashSet<string>(StringComparer.Ordinal) { "admin:*" }
    };
    Expect<ArgumentException>(() => reopened.SaveApproved(escalated, now),
        "unrecognized admin scope refused at persistence boundary");
    var missingRead = restricted with
    {
        DeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
        Scopes = new HashSet<string>(StringComparer.Ordinal) { "conversation:write" }
    };
    Expect<ArgumentException>(() => reopened.SaveApproved(missingRead, now),
        "write-only scope refused at persistence boundary");
    Expect<ArgumentException>(() => reopened.SaveApproved(
        restricted with { DeviceId = "not-an-opaque-id" }, now),
        "malformed device identifier refused");
    Expect<ArgumentException>(() => reopened.SaveApproved(
        restricted with { Revoked = true }, now),
        "already-revoked device cannot be registered");

    Check(!reopened.Revoke(ownerB, full.DeviceId, now),
        "cross-owner revocation denied");
    Check(first.CurrentScopes(ownerA, full.DeviceId) is not null,
        "failed cross-owner revoke did not alter entitlement");

    // Revoke via a different registry instance: old verifier must reread SQL.
    Check(reopened.Revoke(ownerA, full.DeviceId, now),
        "owner can revoke from another process/registry instance");
    Check(first.CurrentScopes(ownerA, full.DeviceId) is null,
        "fresh SQL read invalidates already-known device");
    Check(!reopened.Revoke(ownerA, full.DeviceId, now),
        "repeated revoke is idempotently denied");
    Check(new DurableDeviceRegistry(path).CurrentScopes(ownerA, full.DeviceId) is null,
        "revocation persists across simulated process restart");
    Check(first.CountAudits(ownerA, full.DeviceId) == 2,
        "enrollment and revocation are exactly two audit rows");
    Check(reopened.CountAudits(ownerB, restricted.DeviceId) == 1,
        "second owner audit was unaffected");

    // Revoke a different device from concurrent connections. Exactly one
    // operation succeeds and a single audit event is committed.
    var concurrent = Approve(ownerA, "Concurrent revocation",
        new[] { "conversation:read" });
    first.SaveApproved(concurrent, now);
    var successes = 0;
    Parallel.For(0, 12, _ =>
    {
        var instance = new DurableDeviceRegistry(path);
        if (instance.Revoke(ownerA, concurrent.DeviceId, now))
            Interlocked.Increment(ref successes);
    });
    Check(successes == 1, "parallel revocation succeeds exactly once");
    Check(first.CountAudits(ownerA, concurrent.DeviceId) == 2,
        "parallel revocation produces one event, not duplicated events");
    Check(first.CurrentScopes(ownerA, concurrent.DeviceId) is null,
        "concurrent revocation remains denied after writes commit");

    var corruptPath = Path.Combine(folder, "corrupt-registry.sqlite");
    File.WriteAllText(corruptPath, "not-a-sqlite-database");
    Expect<SqliteException>(() => new DurableDeviceRegistry(corruptPath),
        "corrupt database throws, never turns into an open allowlist");
    var absentPath = Path.Combine(folder, "removed.sqlite");
    var absent = new DurableDeviceRegistry(absentPath, createIfMissing: true);
    File.Delete(absentPath);
    Expect<SqliteException>(() => absent.CurrentScopes(ownerA, full.DeviceId),
        "missing database at verification time denies instead of recreating");

    // Store no private device keys, JWTs or message bodies.
    var contents = File.ReadAllBytes(path);
    var text = System.Text.Encoding.UTF8.GetString(contents);
    Check(!text.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal),
        "database does not contain private signing material");
    Check(!text.Contains("Bearer ", StringComparison.Ordinal),
        "database does not contain access-token strings");

    Console.WriteLine($"PASS: {assertions} synthetic durable enrollment and revocation checks, "
        + "cross-owner isolation, atomic audit, concurrency, fail-closed corruption, restart persistence");
}
finally
{
    try { Directory.Delete(folder, recursive: true); }
    catch (IOException) { /* test-only temp files can be cleaned by CI */ }
}
