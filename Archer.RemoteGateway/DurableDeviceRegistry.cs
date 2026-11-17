using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Archer.RemoteGateway;

/// <summary>
/// LOCAL SYNTHETIC FIXTURE ONLY. Durable SQLite storage of approved device
/// metadata (public-key fingerprint, owner, scopes and revocation), NOT
/// messages, signing keys, refresh tokens or an encrypted production database.
/// All callers must authenticate the owner and complete proof-of-possession
/// BEFORE creating an approved device. Not exposed on HTTP routes.
/// </summary>
internal sealed class DurableDeviceRegistry
{
    private const int SchemaVersion = 1;
    private static readonly HashSet<string> AllowedScopes = new(StringComparer.Ordinal)
    {
        "conversation:read", "conversation:write",
        "notifications:receive", "receipts:write"
    };
    private readonly string _databasePath;

    internal DurableDeviceRegistry(string absolutePath, bool createIfMissing = false)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) ||
            !Path.IsPathFullyQualified(absolutePath) ||
            !Directory.Exists(Path.GetDirectoryName(absolutePath)))
            throw new ArgumentException(
                "A file in an existing absolute local directory is required for the synthetic registry.");

        _databasePath = Path.GetFullPath(absolutePath);
        if (!createIfMissing && !File.Exists(_databasePath))
            throw new InvalidOperationException(
                "The signed identity fixture requires an existing synthetic enrollment registry.");

        using var db = Open(createIfMissing);
        using var version = db.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar());
        if (schema is not (0 or SchemaVersion) ||
            (schema == 0 && !createIfMissing))
            throw new InvalidOperationException(
                "Unsupported or uninitialized synthetic device registry schema.");

        if (schema == 0)
        {
            using var transaction = db.BeginTransaction();
            using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS approved_devices (
                    device_id TEXT PRIMARY KEY NOT NULL,
                    owner_subject TEXT NOT NULL,
                    device_label TEXT NOT NULL,
                    public_key_sha256 TEXT NOT NULL,
                    granted_scopes_json TEXT NOT NULL,
                    enrolled_at_utc TEXT NOT NULL,
                    revoked_at_utc TEXT
                );
                CREATE INDEX IF NOT EXISTS approved_devices_owner
                    ON approved_devices(owner_subject, device_id);
                CREATE TABLE IF NOT EXISTS device_events (
                    event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    device_id TEXT NOT NULL,
                    owner_subject TEXT NOT NULL,
                    action TEXT NOT NULL CHECK(action IN ('enrolled', 'revoked')),
                    event_at_utc TEXT NOT NULL,
                    FOREIGN KEY(device_id) REFERENCES approved_devices(device_id)
                );
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    /// <summary>
    /// Accepts ONLY the output of an already owner-approved pairing flow.
    /// An attacker-controlled DeviceSummary would bypass proof; NEVER expose
    /// this method via a raw request body or a public admin route.
    /// </summary>
    internal void SaveApproved(
        DevicePairingContract.DeviceSummary approved, DateTimeOffset serverNow)
    {
        ValidateDevice(approved);
        if (approved.Revoked || approved.EnrolledAtUtc > serverNow)
            throw new ArgumentException("Only an active, completed synthetic enrollment can be saved.");

        using var db = Open();
        using var transaction = db.BeginTransaction();
        using (var insert = db.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO approved_devices
                    (device_id, owner_subject, device_label, public_key_sha256,
                     granted_scopes_json, enrolled_at_utc, revoked_at_utc)
                VALUES (@id, @owner, @label, @fingerprint, @scopes, @enrolled, NULL);
                """;
            insert.Parameters.AddWithValue("@id", approved.DeviceId);
            insert.Parameters.AddWithValue("@owner", approved.OwnerSubject);
            insert.Parameters.AddWithValue("@label", approved.Label);
            insert.Parameters.AddWithValue("@fingerprint", approved.PublicKeyFingerprint);
            insert.Parameters.AddWithValue("@scopes", JsonSerializer.Serialize(
                approved.Scopes.OrderBy(x => x, StringComparer.Ordinal).ToArray()));
            insert.Parameters.AddWithValue("@enrolled", approved.EnrolledAtUtc.ToUniversalTime()
                .ToString("O"));
            insert.ExecuteNonQuery();
        }

        AddAudit(db, transaction, approved.DeviceId, approved.OwnerSubject,
            "enrolled", serverNow);
        transaction.Commit();
    }

    /// <summary>
    /// Always reopens the persisted store: no cached positive authorization
    /// survives a revocation made by another process. Database faults propagate
    /// to the caller, which MUST fail closed, never fall back to JWT scopes.
    /// </summary>
    internal IReadOnlySet<string>? CurrentScopes(
        string authenticatedOwnerSubject, string deviceId)
    {
        if (!ValidText(authenticatedOwnerSubject, 128) ||
            !IsHex(deviceId, 48))
            return null;

        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT granted_scopes_json FROM approved_devices
            WHERE device_id = @device AND owner_subject = @owner
              AND revoked_at_utc IS NULL
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@device", deviceId);
        command.Parameters.AddWithValue("@owner", authenticatedOwnerSubject);
        var json = command.ExecuteScalar() as string;
        if (json is null) return null;

        string[]? scopes;
        try { scopes = JsonSerializer.Deserialize<string[]>(json); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Malformed synthetic registry scope data.", ex);
        }

        if (scopes is not { Length: > 0 } ||
            scopes.Length > AllowedScopes.Count ||
            scopes.Any(s => s is null || !AllowedScopes.Contains(s)) ||
            (scopes.Contains("conversation:write") &&
             !scopes.Contains("conversation:read")))
            throw new InvalidOperationException(
                "Invalid synthetic registry scope data; access denied.");

        return new HashSet<string>(scopes, StringComparer.Ordinal);
    }

    /// <summary>
    /// Atomic, owner-scoped revocation + audit. Revocation persists after the
    /// gateway, test process or JWT verifier restarts.
    /// </summary>
    internal bool Revoke(
        string trustedOwnerSubject, string deviceId, DateTimeOffset serverNow)
    {
        if (!ValidText(trustedOwnerSubject, 128) || !IsHex(deviceId, 48))
            return false;

        using var db = Open();
        using var transaction = db.BeginTransaction();
        int updated;
        using (var command = db.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE approved_devices
                SET revoked_at_utc = @time
                WHERE device_id = @device AND owner_subject = @owner
                    AND revoked_at_utc IS NULL;
                """;
            command.Parameters.AddWithValue("@time", serverNow.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("@device", deviceId);
            command.Parameters.AddWithValue("@owner", trustedOwnerSubject);
            updated = command.ExecuteNonQuery();
        }

        if (updated == 1)
            AddAudit(db, transaction, deviceId, trustedOwnerSubject, "revoked", serverNow);
        transaction.Commit();
        return updated == 1;
    }

    internal int CountAudits(string ownerSubject, string deviceId)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM device_events
            WHERE owner_subject = @owner AND device_id = @device;
            """;
        command.Parameters.AddWithValue("@owner", ownerSubject);
        command.Parameters.AddWithValue("@device", deviceId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void AddAudit(
        SqliteConnection db, SqliteTransaction transaction, string deviceId,
        string ownerSubject, string action, DateTimeOffset when)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO device_events(device_id, owner_subject, action, event_at_utc)
            VALUES (@device, @owner, @action, @when);
            """;
        command.Parameters.AddWithValue("@device", deviceId);
        command.Parameters.AddWithValue("@owner", ownerSubject);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@when", when.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open(bool create = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());

        try
        {
            connection.Open();
            using var pragmas = connection.CreateCommand();
            pragmas.CommandText = """
                PRAGMA busy_timeout = 5000;
                PRAGMA foreign_keys = ON;
                PRAGMA synchronous = FULL;
                """;
            pragmas.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void ValidateDevice(DevicePairingContract.DeviceSummary device)
    {
        if (device is null || !IsHex(device.DeviceId, 48) ||
            !IsHex(device.PublicKeyFingerprint, 64) ||
            !ValidText(device.OwnerSubject, 128) ||
            !ValidText(device.Label, 80) ||
            device.Scopes is null || device.Scopes.Count == 0 ||
            device.Scopes.Count > AllowedScopes.Count ||
            device.Scopes.Any(scope => !AllowedScopes.Contains(scope)) ||
            (device.Scopes.Contains("conversation:write") &&
             !device.Scopes.Contains("conversation:read")))
            throw new ArgumentException("Invalid approved synthetic device metadata.");
    }

    private static bool ValidText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maxLength && !value.Any(char.IsControl);

    private static bool IsHex(string? value, int length) =>
        value is not null && value.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
