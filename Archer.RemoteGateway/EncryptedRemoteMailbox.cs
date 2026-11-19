using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Archer.RemoteGateway;

/// <summary>
/// Local single-instance private mailbox proof of concept. An owner-authenticated,
/// separately approved LOCAL operator can enqueue a message. Only the owner
/// and a currently enrolled, signed-read authorized device can read it.
/// Bodies are AES-256-GCM encrypted; associated data binds owner, ID and date.
/// No chat model, transport delivery, websocket, SMS or live PC sync.
/// </summary>
internal sealed class EncryptedRemoteMailbox
{
    private const int MaxMessages = 2000;
    private const int MaxReadLimit = 50;
    private const int MaxMessageLength = 1200;
    private const int RetentionDays = 7;
    private readonly string _path;
    private readonly byte[] _key;

    internal sealed record Message(
        long Sequence, string Id, string Role, string Content, DateTimeOffset CreatedAtUtc);
    internal sealed record Receipt(long Sequence, string Id, DateTimeOffset CreatedAtUtc);

    internal EncryptedRemoteMailbox(LocalMailboxSettings options)
    {
        _path = options.DatabasePath;
        _key = (byte[])options.Key.Clone();
        if (_key.Length != 32)
            throw new InvalidOperationException("Mailbox key must be AES-256.");
        using var db = Open(true);
        using var version = db.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar());
        if (schema is not (0 or 1))
            throw new InvalidOperationException("Unsupported private mailbox schema.");
        if (schema == 0)
        {
            using var tx = db.BeginTransaction();
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS messages (
                    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                    message_id TEXT NOT NULL UNIQUE,
                    owner_subject TEXT NOT NULL,
                    role TEXT NOT NULL CHECK(role IN ('operator')),
                    created_unix_ms INTEGER NOT NULL,
                    nonce BLOB NOT NULL,
                    ciphertext BLOB NOT NULL,
                    auth_tag BLOB NOT NULL
                );
                CREATE INDEX IF NOT EXISTS messages_owner_seq
                    ON messages(owner_subject, sequence);
                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    internal Receipt Enqueue(
        string verifiedOwnerSubject, string plaintext, DateTimeOffset serverNow)
    {
        ValidateOwner(verifiedOwnerSubject);
        if (string.IsNullOrWhiteSpace(plaintext) ||
            plaintext.Length > MaxMessageLength ||
            plaintext.Any(c => char.IsControl(c) && c != '\n'))
            throw new ArgumentException("Invalid bounded mailbox message.");

        var id = Guid.NewGuid().ToString("N");
        var unixMs = serverNow.ToUnixTimeMilliseconds();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var encoded = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = new byte[encoded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, 16))
            aes.Encrypt(nonce, encoded, encrypted, tag, AdditionalData(
                verifiedOwnerSubject, id, unixMs));
        CryptographicOperations.ZeroMemory(encoded);

        using var db = Open();
        using var transaction = db.BeginTransaction();
        using (var purge = db.CreateCommand())
        {
            purge.Transaction = transaction;
            purge.CommandText = "DELETE FROM messages WHERE created_unix_ms < @cutoff";
            purge.Parameters.AddWithValue("@cutoff",
                serverNow.AddDays(-RetentionDays).ToUnixTimeMilliseconds());
            purge.ExecuteNonQuery();
        }
        using (var count = db.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM messages";
            if (Convert.ToInt64(count.ExecuteScalar()) >= MaxMessages)
                throw new InvalidOperationException("Private mailbox capacity reached.");
        }
        using (var insert = db.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO messages (message_id, owner_subject, role, created_unix_ms,
                                      nonce, ciphertext, auth_tag)
                VALUES (@id, @owner, 'operator', @time, @nonce, @cipher, @tag);
                """;
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@owner", verifiedOwnerSubject);
            insert.Parameters.AddWithValue("@time", unixMs);
            insert.Parameters.AddWithValue("@nonce", nonce);
            insert.Parameters.AddWithValue("@cipher", encrypted);
            insert.Parameters.AddWithValue("@tag", tag);
            insert.ExecuteNonQuery();
        }
        long sequence;
        using (var last = db.CreateCommand())
        {
            last.Transaction = transaction;
            last.CommandText = "SELECT last_insert_rowid()";
            sequence = Convert.ToInt64(last.ExecuteScalar());
        }
        transaction.Commit();
        return new Receipt(sequence, id, serverNow);
    }

    internal IReadOnlyList<Message> Read(
        string verifiedOwnerSubject, long after, int limit, DateTimeOffset serverNow)
    {
        ValidateOwner(verifiedOwnerSubject);
        if (after < 0 || limit is < 1 or > MaxReadLimit)
            throw new ArgumentException("Invalid mailbox page request.");
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT sequence, message_id, role, created_unix_ms, nonce, ciphertext, auth_tag
            FROM messages
            WHERE owner_subject = @owner AND sequence > @after
              AND created_unix_ms >= @cutoff AND created_unix_ms <= @now
            ORDER BY sequence LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@owner", verifiedOwnerSubject);
        cmd.Parameters.AddWithValue("@after", after);
        cmd.Parameters.AddWithValue("@cutoff",
            serverNow.AddDays(-RetentionDays).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("@now", serverNow.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("@limit", limit);
        var result = new List<Message>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var seq = reader.GetInt64(0);
            var id = reader.GetString(1);
            var role = reader.GetString(2);
            var ms = reader.GetInt64(3);
            var nonce = (byte[])reader[4];
            var ciphertext = (byte[])reader[5];
            var tag = (byte[])reader[6];
            if (nonce.Length != 12 || tag.Length != 16 ||
                ciphertext.Length > MaxMessageLength * 4 ||
                role != "operator")
                throw new CryptographicException("Corrupt encrypted mailbox row.");
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(_key, 16))
                aes.Decrypt(nonce, ciphertext, tag, plaintext,
                    AdditionalData(verifiedOwnerSubject, id, ms));
            var content = Encoding.UTF8.GetString(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);
            result.Add(new Message(seq, id, role, content,
                DateTimeOffset.FromUnixTimeMilliseconds(ms)));
        }
        return result;
    }

    private static byte[] AdditionalData(string owner, string id, long ms) =>
        Encoding.UTF8.GetBytes("ARCHER_REMOTE_MAILBOX_V1\n" + owner + "\n" +
            id + "\n" + ms.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void ValidateOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) ||
            owner.Length > 128 || owner.Any(char.IsControl))
            throw new ArgumentException("Invalid verified mailbox owner.");
    }

    private SqliteConnection Open(bool create = false)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        try
        {
            db.Open();
            using var pragmas = db.CreateCommand();
            pragmas.CommandText = """
                PRAGMA busy_timeout = 5000;
                PRAGMA foreign_keys = ON;
                PRAGMA synchronous = FULL;
                """;
            pragmas.ExecuteNonQuery();
            return db;
        }
        catch { db.Dispose(); throw; }
    }
}
