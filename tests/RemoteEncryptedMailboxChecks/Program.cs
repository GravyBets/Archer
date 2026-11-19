using System.Security.Cryptography;
using System.Text;
using Archer.RemoteGateway;
using Microsoft.Data.Sqlite;

const string ownerA = "auth0|synthetic-owner-a";
const string ownerB = "auth0|synthetic-owner-b";
const string sensitive = "PRIVATE_TEST_MESSAGE_never_in_plaintext_sqlite_bytes_83B2";
var now = new DateTimeOffset(2026, 11, 18, 20, 0, 0, TimeSpan.Zero);
var checks = 0;
void Check(bool yes, string label)
{
    checks++;
    if (!yes) throw new InvalidOperationException("FAIL: " + label);
}
void Reject<T>(Action op, string label) where T : Exception
{
    var failed = false;
    try { op(); } catch (T) { failed = true; }
    Check(failed, label);
}
using var temp = new TempFolder();
var path = Path.Combine(temp.Path, "private-mailbox.sqlite");
var key = RandomNumberGenerator.GetBytes(32);
var hex = Convert.ToHexString(key);
var env = new Dictionary<string, string?>
{
    ["ARCHER_REMOTE_GATEWAY_MODE"] = "local-test",
    ["ARCHER_REMOTE_MAILBOX_ENABLED"] = "local-readonly",
    ["ARCHER_REMOTE_MAILBOX_FILE"] = path,
    ["ARCHER_REMOTE_MAILBOX_KEY_HEX"] = hex
};
LocalMailboxSettings? Settings(IDictionary<string,string?> values,
    bool dev = true, bool authorized = true) =>
    LocalMailboxSettings.FromEnvironment(dev, authorized,
        k => values.TryGetValue(k, out var value) ? value : null);
Check(LocalMailboxSettings.FromEnvironment(true, true, _ => null) is null,
    "disabled by default");
var options = Settings(env)!;
Check(!options.ToString().Contains(hex), "mailbox settings redacts secret");
Check(options.Key.Length == 32, "AES-256 material exists only in server settings");

var store = new EncryptedRemoteMailbox(options);
var first = store.Enqueue(ownerA, sensitive, now);
var second = store.Enqueue(ownerA, "Second private line\nnew line", now);
Check(first.Sequence > 0 && second.Sequence > first.Sequence,
    "server-owned monotonic sequence");
var cross = store.Enqueue(ownerB, "Other owner text", now);
Check(store.Read(ownerA, 0, 20, now).Count == 2,
    "owner gets only own records");
Check(store.Read(ownerB, 0, 20, now).Count == 1,
    "other owner sees only separate messages");
var firstPage = store.Read(ownerA, 0, 1, now);
Check(firstPage.Count == 1 && firstPage[0].Content == sensitive,
    "AES-GCM ciphertext decrypts to correct owner text");
Check(store.Read(ownerA, firstPage[0].Sequence, 1, now)[0].Id ==
      second.Id, "cursor returns next owned message");
Check(store.Read(ownerA, second.Sequence, 2, now).Count == 0,
    "high-watermark returns empty page");
Check(new EncryptedRemoteMailbox(options)
    .Read(ownerA, 0, 5, now).Count == 2,
    "messages survive process/store reopening");
Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(sensitive),
    "no plaintext message in database");
Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("Bearer "),
    "database contains no API bearer credentials");

Reject<ArgumentException>(() => store.Enqueue(ownerA, "", now),
    "empty message denied");
Reject<ArgumentException>(() => store.Enqueue(ownerA, new string('x', 1201), now),
    "oversized message denied");
Reject<ArgumentException>(() => store.Enqueue(ownerA, "forbidden\rcontrol", now),
    "control characters denied");
Reject<ArgumentException>(() => store.Read(ownerA, -1, 5, now),
    "negative cursor denied");
Reject<ArgumentException>(() => store.Read(ownerA, 0, 51, now),
    "unbounded page denied");
Check(store.Read(ownerA, 0, 20, now.AddDays(8)).Count == 0,
    "expired messages are not returned");

var bad = new Dictionary<string,string?>(env);
bad["ARCHER_REMOTE_MAILBOX_KEY_HEX"] = new string('A', 64);
Reject<InvalidOperationException>(() => Settings(bad),
    "low-diversity key cannot initialize");
bad["ARCHER_REMOTE_MAILBOX_KEY_HEX"] = hex;
bad["ARCHER_REMOTE_MAILBOX_ENABLED"] = "public";
Reject<InvalidOperationException>(() => Settings(bad),
    "public mailbox flag rejected");
Reject<InvalidOperationException>(() => Settings(env, dev:false),
    "production environment rejected");
Reject<InvalidOperationException>(() => Settings(env, authorized:false),
    "unverified owner/pairing state rejected");

// Valid but DIFFERENT 256-bit key cannot decrypt persisted ciphertext.
var different = new Dictionary<string,string?>(env);
var altKey = RandomNumberGenerator.GetBytes(32);
while (altKey.SequenceEqual(key)) altKey = RandomNumberGenerator.GetBytes(32);
different["ARCHER_REMOTE_MAILBOX_KEY_HEX"] = Convert.ToHexString(altKey);
var incorrectStore = new EncryptedRemoteMailbox(Settings(different)!);
Reject<CryptographicException>(() => incorrectStore.Read(ownerA, 0, 1, now),
    "wrong encryption key fails closed");

// Ciphertext tampering must never return corrupt plaintext.
using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = path, Mode = SqliteOpenMode.ReadWrite
}.ToString()))
{
    db.Open();
    using var update = db.CreateCommand();
    update.CommandText = "UPDATE messages SET ciphertext = @bad WHERE message_id = @id";
    update.Parameters.AddWithValue("@bad", new byte[] { 1, 2, 3 });
    update.Parameters.AddWithValue("@id", first.Id);
    Check(update.ExecuteNonQuery() == 1, "synthetic ciphertext tampered once");
}
Reject<CryptographicException>(() => store.Read(ownerA, 0, 3, now),
    "AEAD tag rejects tampered stored content");
Check(store.Read(ownerB, 0, 3, now).Count == 1,
    "other owner's records unaffected by corruption");

Console.WriteLine($"PASS: {checks} encrypted, owner-scoped, bounded, cursor-based " +
    "private mailbox tests; no remote service or AI calls.");

internal sealed class TempFolder : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "archer-encrypted-mailbox-" + Guid.NewGuid().ToString("N"));
    internal TempFolder() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
