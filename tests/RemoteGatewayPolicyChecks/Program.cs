using Archer.RemoteGateway;

var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
var read = new HashSet<string>(StringComparer.Ordinal) { "conversation:read" };
var both = new HashSet<string>(StringComparer.Ordinal)
{
    "conversation:read", "conversation:write"
};

var owner = "owner-synthetic-A";
var valid = new TrustedDeviceIdentity(
    owner, "device-synthetic-1", now.AddMinutes(15),
    DeviceRevoked: false, Scopes: both);
var checks = 0;
void Check(bool condition, string scenario)
{
    checks++;
    if (!condition) throw new InvalidOperationException("FAIL: " + scenario);
}

Check(AccessPolicy.CanReadConversation(valid, owner, now),
    "valid identity can read own session");
Check(AccessPolicy.CanWriteConversation(valid, owner, now),
    "valid identity can write own session with both scopes");
Check(!AccessPolicy.CanReadConversation(valid, "owner-synthetic-B", now),
    "cross-owner reads denied");
Check(!AccessPolicy.CanWriteConversation(valid, "owner-synthetic-B", now),
    "cross-owner writes denied");
Check(!AccessPolicy.CanReadConversation(valid, "OWNER-SYNTHETIC-A", now),
    "subject matching is case sensitive");
Check(!AccessPolicy.CanReadConversation(null, owner, now),
    "missing identity denied");
Check(!AccessPolicy.CanReadConversation(valid with { Subject = "" }, owner, now),
    "blank subject denied");
Check(!AccessPolicy.CanReadConversation(valid with { DeviceId = "" }, owner, now),
    "blank device denied");
Check(!AccessPolicy.CanReadConversation(valid, " ", now),
    "blank target owner denied");
Check(!AccessPolicy.CanReadConversation(valid with { DeviceRevoked = true }, owner, now),
    "revoked device read denied");
Check(!AccessPolicy.CanWriteConversation(valid with { DeviceRevoked = true }, owner, now),
    "revoked device write denied");
Check(!AccessPolicy.CanReadConversation(valid with { ExpiresAtUtc = now }, owner, now),
    "expired-at-now token denied");
Check(!AccessPolicy.CanReadConversation(valid with { ExpiresAtUtc = now.AddSeconds(-1) }, owner, now),
    "older expired token denied");
Check(AccessPolicy.CanReadConversation(valid with { Scopes = read }, owner, now),
    "read-only scope can read");
Check(!AccessPolicy.CanWriteConversation(valid with { Scopes = read }, owner, now),
    "read-only scope cannot write");
Check(!AccessPolicy.CanReadConversation(valid with
    { Scopes = new HashSet<string>(StringComparer.Ordinal) { "CONVERSATION:READ" } },
    owner, now), "scope casing must match");
Check(!AccessPolicy.CanReadConversation(valid with
    { Scopes = new HashSet<string>(StringComparer.Ordinal) { "inbox:read" } },
    owner, now), "other scope does not grant conversation read");
Check(!AccessPolicy.CanWriteConversation(valid with
    { Scopes = new HashSet<string>(StringComparer.Ordinal) { "conversation:write" } },
    owner, now), "write without read denied");
Check(!AccessPolicy.CanWriteConversation(valid with
    { Scopes = new HashSet<string>(StringComparer.Ordinal) }, owner, now),
    "empty scopes denied");

Console.WriteLine($"PASS: {checks} synthetic authorization contract checks. No real JWT or network identity configured.");
