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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public GatewayStore(IWebHostEnvironment env)
    {
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
        if (!File.Exists(_path)) return new GatewayState();
        var source = await File.ReadAllTextAsync(_path, ct);
        return JsonSerializer.Deserialize<GatewayState>(source, Json) ?? new GatewayState();
    }

    private async Task SaveAsync(GatewayState state, CancellationToken ct)
    {
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Json), ct);
        File.Move(temp, _path, overwrite: true);
    }
}

public sealed class GatewayState
{
    public List<GatewayDevice> Devices { get; set; } = [];
    public List<GatewayEvent> Events { get; set; } = [];
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

public sealed record EnrollDeviceRequest(string? Label, int LifetimeDays = 7);
public sealed record IncomingGatewayEvent(
    string? SourceKey, string? Category, string? Priority, string? Title,
    string? Summary, DateTimeOffset OccurredAtUtc, DateTimeOffset? ResolvedAtUtc);
public sealed record SyncGatewayEventsRequest(List<IncomingGatewayEvent>? Events);

public sealed class DeviceTokenService(GatewayStore store)
{
    public sealed record EnrolledDevice(Guid Id, string Label, DateTimeOffset ExpiresAtUtc,
        string[] Scopes, string Token);

    private static string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<EnrolledDevice> EnrollAsync(
        string label, int lifetimeDays, CancellationToken ct)
    {
        if (label.Length is < 2 or > 48 || label.Any(char.IsControl))
            throw new ArgumentException("Device label must be 2–48 visible characters.");
        if (lifetimeDays is < 1 or > 7)
            throw new ArgumentException("Device token lifetime must be 1–7 days.");

        // Generated exactly once, returned only to the locally authenticated
        // provisioning request. Never printed by the server or returned by listings.
        var token = "archer_v1_" + Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var device = new GatewayDevice
        {
            Label = label,
            TokenHashHex = Hash(token),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(lifetimeDays)
        };
        var created = await store.UpdateAsync(state =>
        {
            if (state.Devices.Count(d =>
                d.RevokedAtUtc is null && d.ExpiresAtUtc > DateTimeOffset.UtcNow) >= 5)
                return false;
            state.Devices.Add(device);
            if (state.Devices.Count > 100)
                state.Devices = state.Devices.OrderByDescending(d => d.EnrolledAtUtc)
                    .Take(100).ToList();
            return true;
        }, ct);
        if (!created) throw new InvalidOperationException("Maximum five active devices reached.");
        return new EnrolledDevice(device.Id, device.Label, device.ExpiresAtUtc, device.Scopes, token);
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
