using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Archer.MobileGateway;

/// <summary>
/// Separate gateway-owned data. No access to Archer's admin state, database,
/// AI keys, code writer, Twilio credentials, or raw phone/location data.
/// All mutations are serialized and saved atomically in a Git-ignored directory.
/// </summary>
public sealed class GatewayStore
{
    private readonly string _path;
    private readonly GatewayStateEncryption _encryption;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public GatewayStore(IWebHostEnvironment env, GatewayStateEncryption encryption)
    {
        _encryption = encryption;
        var configured = Environment.GetEnvironmentVariable("ARCHER_GATEWAY_DATA_DIR");
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "Data")
            : Path.GetFullPath(configured);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "mobile-gateway-state.json");
    }

    public async Task<T> ReadAsync<T>(Func<GatewayState, T> read, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return read(await LoadAsync(ct)); }
        finally { _gate.Release(); }
    }

    public async Task<T> UpdateAsync<T>(Func<GatewayState, T> update, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            var result = update(state);
            await SaveAsync(state, ct);
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task<GatewayState> LoadAsync(CancellationToken ct)
    {
        // Never access or mutate the on-disk gateway state without an owner-
        // configured encryption key. Health checks can run without one.
        _encryption.RequireKey();
        if (!File.Exists(_path)) return new GatewayState();

        var source = await File.ReadAllTextAsync(_path, ct);
        var (plainJson, legacy) = _encryption.DecryptOrIdentifyLegacy(source);
        var state = JsonSerializer.Deserialize<GatewayState>(plainJson, Json) ??
            throw new InvalidDataException("Gateway state could not be parsed. It will not be reset.");

        if (legacy)
        {
            // v1.5 plaintext migration: encrypt and atomically replace the
            // canonical file BEFORE returning any state data to an API.
            // Existing file-system backups/snapshots may still contain
            // plaintext: they must be handled by the owner separately.
            await SaveAsync(state, ct);
        }

        return state;
    }

    private async Task SaveAsync(GatewayState state, CancellationToken ct)
    {
        _encryption.RequireKey();
        var plaintext = JsonSerializer.Serialize(state, Json);
        var encrypted = _encryption.Encrypt(plaintext);
        // A unique temp name avoids collision with an interrupted old write.
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, encrypted, ct);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            // A failed save leaves the previous encrypted state untouched.
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

public sealed class GatewayState
{
    public List<GatewayDevice> Devices { get; set; } = [];
    public List<GatewayEvent> Events { get; set; } = [];
    // Readable notification routing SIMULATIONS. No provider messages or push tokens.
    public List<GatewayDeliveryPreview> DeliveryPreviews { get; set; } = [];
    // Audit metadata only: never credentials, full notification bodies or locations.
    public List<GatewayAuditEntry> Audit { get; set; } = [];
    // Opt-in local messages only. No AI calls or automatic desktop chat synchronization.
    public List<GatewayConversationMessage> ConversationMessages { get; set; } = [];
}

public sealed class GatewayDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    // SHA-256 of a random 256-bit bearer. NEVER store or log the original token.
    public string TokenHashHex { get; set; } = "";
    public string[] Scopes { get; set; } = ["inbox:read", "inbox:ack"];
    public DateTimeOffset EnrolledAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset? RotatedAtUtc { get; set; }
}

public sealed class GatewayEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceKey { get; set; } = "";
    public string Category { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
}

public sealed record EnrollDeviceRequest(string? Label, int LifetimeDays = 7,
    string[]? Scopes = null);
public sealed record RotateDeviceRequest(int LifetimeDays = 7);
public sealed record IncomingGatewayEvent(
    string? SourceKey, string? Category, string? Priority, string? Title,
    string? Summary, DateTimeOffset OccurredAtUtc, DateTimeOffset? ResolvedAtUtc);
public sealed record SyncGatewayEventsRequest(List<IncomingGatewayEvent>? Events);

public sealed class GatewayConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Guid ClientMessageId { get; set; }
    public string Role { get; set; } = "owner";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record AppendConversationMessageRequest(
    Guid ClientMessageId, string? Content);

public sealed class GatewayAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Action { get; set; } = "";
    public Guid? DeviceId { get; set; }
    public Guid? EventId { get; set; }
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>No sensitive text/secret content ever enters audit history.</summary>
public static class GatewayAudit
{
    public static void Add(GatewayState state, string action,
        Guid? deviceId = null, Guid? eventId = null)
    {
        state.Audit.Insert(0, new GatewayAuditEntry
        {
            Action = action,
            DeviceId = deviceId,
            EventId = eventId
        });
        if (state.Audit.Count > 300)
            state.Audit.RemoveRange(300, state.Audit.Count - 300);
    }
}

public sealed class DeviceTokenService(GatewayStore store)
{
    public sealed record EnrolledDevice(Guid Id, string Label, DateTimeOffset ExpiresAtUtc,
        string[] Scopes, string Token);

    private static string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static readonly string[] DefaultScopes = ["inbox:read", "inbox:ack"];
    private static readonly string[] PermittedScopes =
        ["inbox:read", "inbox:ack", "conversation:read", "conversation:write"];

    private static string[] NormalizeScopes(string[]? requested)
    {
        var scopes = requested ?? DefaultScopes;
        if (scopes.Length is < 1 or > 4 ||
            scopes.Any(x => !PermittedScopes.Contains(x, StringComparer.Ordinal)) ||
            scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Length ||
            (scopes.Contains("inbox:ack", StringComparer.Ordinal) &&
                !scopes.Contains("inbox:read", StringComparer.Ordinal)) ||
            (scopes.Contains("conversation:write", StringComparer.Ordinal) &&
                !scopes.Contains("conversation:read", StringComparer.Ordinal)))
            throw new ArgumentException("Supported scopes: inbox:read, inbox:ack (requires inbox:read), conversation:read, conversation:write (requires conversation:read).");
        return scopes;
    }

    private static string NewToken() =>
        "archer_v1_" + Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public async Task<EnrolledDevice> EnrollAsync(
        string label, int lifetimeDays, string[]? requestedScopes, CancellationToken ct)
    {
        if (label.Length is < 2 or > 48 || label.Any(char.IsControl))
            throw new ArgumentException("Device label must be 2–48 visible characters.");
        if (lifetimeDays is < 1 or > 7)
            throw new ArgumentException("Device token lifetime must be 1–7 days.");

        var scopes = NormalizeScopes(requestedScopes);
        // Generated exactly once. Never print, echo on listing, or persist raw.
        var token = NewToken();
        var device = new GatewayDevice
        {
            Label = label,
            TokenHashHex = Hash(token),
            Scopes = scopes,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(lifetimeDays)
        };
        var created = await store.UpdateAsync(state =>
        {
            if (state.Devices.Count(d =>
                d.RevokedAtUtc is null && d.ExpiresAtUtc > DateTimeOffset.UtcNow) >= 5)
                return false;
            state.Devices.Add(device);
            GatewayAudit.Add(state, "device.enrolled", device.Id);
            if (state.Devices.Count > 100)
                state.Devices = state.Devices.OrderByDescending(d => d.EnrolledAtUtc)
                    .Take(100).ToList();
            return true;
        }, ct);
        if (!created) throw new InvalidOperationException("Maximum five active devices reached.");
        return new EnrolledDevice(device.Id, device.Label, device.ExpiresAtUtc, device.Scopes, token);
    }

    /// <summary>
    /// Only a local administrator can rotate an existing ACTIVE device.
    /// The previous bearer is invalidated in the same atomic state update.
    /// The new bearer is shown once and never persisted in plaintext.
    /// </summary>
    public async Task<EnrolledDevice?> RotateAsync(
        Guid id, int lifetimeDays, CancellationToken ct)
    {
        if (lifetimeDays is < 1 or > 7)
            throw new ArgumentException("Device token lifetime must be 1–7 days.");

        var token = NewToken();
        var rotatedAt = DateTimeOffset.UtcNow;
        var device = await store.UpdateAsync(state =>
        {
            var existing = state.Devices.FirstOrDefault(x =>
                x.Id == id && x.RevokedAtUtc is null && x.ExpiresAtUtc > rotatedAt);
            if (existing is null) return null;

            existing.TokenHashHex = Hash(token);
            existing.RotatedAtUtc = rotatedAt;
            existing.ExpiresAtUtc = rotatedAt.AddDays(lifetimeDays);
            GatewayAudit.Add(state, "device.rotated", existing.Id);
            return existing;
        }, ct);
        return device is null ? null
            : new EnrolledDevice(device.Id, device.Label,
                device.ExpiresAtUtc, device.Scopes, token);
    }

    public async Task<GatewayDevice?> AuthenticateAsync(string? authorization,
        string requiredScope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.Ordinal) ||
            authorization.Length > 256)
            return null;
        var supplied = authorization["Bearer ".Length..].Trim();
        if (!supplied.StartsWith("archer_v1_", StringComparison.Ordinal) ||
            supplied.Length != 74)
            return null;
        var bytes = Convert.FromHexString(Hash(supplied));
        return await store.ReadAsync(state =>
            state.Devices.FirstOrDefault(device =>
                device.RevokedAtUtc is null &&
                device.ExpiresAtUtc > DateTimeOffset.UtcNow &&
                device.Scopes.Contains(requiredScope, StringComparer.Ordinal) &&
                device.TokenHashHex.Length == 64 &&
                CryptographicOperations.FixedTimeEquals(
                    bytes, Convert.FromHexString(device.TokenHashHex))), ct);
    }
}

public sealed class GatewayDeliveryPreview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid DeviceId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string Channel { get; set; } = "push-preview";
    public string State { get; set; } = "Simulated";
    public string Reason { get; set; } = "";
    public bool InterruptOwner { get; set; }
    public bool RequiresIncidentVerification { get; set; }
    public DateTimeOffset SimulatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record SimulateDeliveryRequest(bool Confirm, int MaxEvents = 10);
