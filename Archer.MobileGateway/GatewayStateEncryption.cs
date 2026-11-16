using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Archer.MobileGateway;

/// <summary>
/// Encrypts the ENTIRE separately stored gateway state with AES-256-GCM.
/// This protects messages, event summaries, device token hashes and audit
/// metadata together. The key is a 32-byte (64 hex chars) environment secret.
/// No password-derived/default/repo key exists. Neither the key nor the
/// decrypted bytes are returned to devices or logged.
/// </summary>
public sealed class GatewayStateEncryption
{
    private const string FormatName = "archer-gateway-aes256gcm-v1";
    private static readonly byte[] AssociatedData =
        Encoding.UTF8.GetBytes("Archer.MobileGateway.State.v1");
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly byte[]? _key;

    private sealed class EncryptedEnvelope
    {
        public string Format { get; set; } = FormatName;
        public string Nonce { get; set; } = "";
        public string Ciphertext { get; set; } = "";
        public string Tag { get; set; } = "";
    }

    public GatewayStateEncryption()
    {
        var hex = Environment.GetEnvironmentVariable("ARCHER_GATEWAY_DATA_KEY_HEX");
        if (string.IsNullOrWhiteSpace(hex)) return;

        if (hex.Length != 64 ||
            !hex.All(c => char.IsAsciiHexDigit(c)))
            throw new InvalidOperationException(
                "ARCHER_GATEWAY_DATA_KEY_HEX must contain exactly 64 hex characters (a random 256-bit key). Never use a sample or check the key into Git.");

        _key = Convert.FromHexString(hex);
    }

    public bool IsReady => _key is { Length: 32 };

    public void RequireKey()
    {
        if (!IsReady)
            throw new InvalidOperationException(
                "Archer mobile gateway state encryption is not configured. Set ARCHER_GATEWAY_DATA_KEY_HEX in the private process environment; no state writes are permitted without it.");
    }

    public string Encrypt(string json)
    {
        RequireKey();
        var plain = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        try
        {
            using var aes = new AesGcm(_key!, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData);
            return JsonSerializer.Serialize(new EncryptedEnvelope
            {
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(cipher),
                Tag = Convert.ToBase64String(tag)
            }, Json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// Legacy plaintext JSON is accepted ONLY for a one-time local migration
    /// from v1.5. Caller MUST encrypt and replace it before serving any data.
    /// An encrypted envelope with wrong key, malformed data or future format
    /// fails closed; it NEVER falls back to a fresh empty state.
    /// </summary>
    public (string Json, bool Legacy) DecryptOrIdentifyLegacy(string content)
    {
        RequireKey();
        using var parsed = JsonDocument.Parse(content);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Gateway state root must be an object.");

        if (!parsed.RootElement.TryGetProperty("format", out var format))
        {
            // Only an existing, recognizable v1.5 state file may migrate.
            if (!parsed.RootElement.TryGetProperty("devices", out _) &&
                !parsed.RootElement.TryGetProperty("events", out _) &&
                !parsed.RootElement.TryGetProperty("conversationMessages", out _))
                throw new InvalidDataException(
                    "Gateway state does not match a supported previous format; refusing to overwrite.");
            return (content, true);
        }

        if (format.ValueKind != JsonValueKind.String ||
            format.GetString() != FormatName)
            throw new InvalidDataException(
                "Gateway state uses an unsupported encrypted format. Refusing to overwrite.");

        // The envelope fields are entirely untrusted; malformed base64,
        // length, or authentication must fail without returning any state.
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(content, Json) ??
            throw new InvalidDataException("Encrypted state envelope is missing.");
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var cipher = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        if (nonce.Length != 12 || tag.Length != 16 || cipher.Length == 0)
            throw new InvalidDataException("Encrypted gateway state has invalid field sizes.");

        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(_key!, tag.Length);
            aes.Decrypt(nonce, cipher, tag, plain, AssociatedData);
            return (Encoding.UTF8.GetString(plain), false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
