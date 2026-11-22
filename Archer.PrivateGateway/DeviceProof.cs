using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Archer.PrivateGateway;

// The owner provisions one exact P-256 public-key fingerprint privately on
// the host. A valid bearer token alone is insufficient to read or write.
internal sealed class DeviceProof(string approvedHash)
{
    internal const string Overview = "/v1/overview", Message = "/v1/message", Receipt = "/v1/receipt";
    internal static string EmptyDigest => Hash([]);
    private readonly object gate = new();
    private readonly Dictionary<string, Challenge> pending = new(StringComparer.Ordinal);
    internal sealed record Challenge(string Ticket, string Nonce, string Method, string Path, string BodyHash, DateTimeOffset Expires);
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool Allowed(string method,string path) => (method == "GET" && path == Overview) || (method == "POST" && (path == Message || path == Receipt));
    internal Challenge? Begin(string method, string path, string bodyHash, DateTimeOffset now)
    {
        if (!Allowed(method,path) || !Regex.IsMatch(bodyHash ?? "", "^[A-F0-9]{64}$") ||
            (method == "GET" && bodyHash != EmptyDigest)) return null;
        lock (gate)
        {
            foreach (var key in pending.Where(x => x.Value.Expires <= now).Select(x => x.Key).ToArray()) pending.Remove(key);
            if (pending.Count >= 32) return null;
            var c = new Challenge(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),method,path,bodyHash!,now.AddSeconds(90));
            pending.Add(c.Ticket,c); return c;
        }
    }
    internal static byte[] Payload(Challenge c) => Encoding.UTF8.GetBytes(
        "ARCHER_PRIVATE_V1\n" + c.Ticket + "\n" + c.Nonce + "\n" + c.Method + "\n" + c.Path + "\n" + c.BodyHash);
    internal bool Consume(string ticket, string method, string path, byte[] body, string keyHeader, string proofHeader, DateTimeOffset now)
    {
        Challenge? c;
        lock(gate) { if (!pending.Remove(ticket,out c)) return false; }
        // Consume on every attempt (including invalid proof). No replay.
        if (c.Expires <= now || c.Method != method || c.Path != path || c.BodyHash != Hash(body) ||
            keyHeader.Length > 120 || proofHeader.Length > 120) return false;
        try
        {
            var key = Convert.FromBase64String(keyHeader);
            var signature = Convert.FromBase64String(proofHeader);
            if (key.Length != 65 || key[0] != 4 || signature.Length != 64 ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(key),Convert.FromHexString(approvedHash))) return false;
            using var verifier = ECDsa.Create(new ECParameters {
                Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33], Y = key[33..65] }
            });
            return verifier.VerifyData(Payload(c),signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or CryptographicException) { return false; }
    }
}