using System.Text.RegularExpressions;
using Archer.RemoteGateway;

namespace Archer.PrivateGateway;

internal sealed record PrivateSettings(string Host, string BridgeKey, string DeviceHash, Auth0OwnerSignInSettings Owner)
{
    internal static PrivateSettings Load(Func<string,string?> get)
    {
        if (get("ARCHER_PRIVATE_GATEWAY_ENABLED") != "true")
            throw new InvalidOperationException("Private gateway disabled.");
        string Required(string name, string pattern)
        {
            var value = get(name) ?? "";
            if (value.Length > 256 || !Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new InvalidOperationException("Invalid or missing private gateway setting: " + name);
            return value;
        }
        var host = Required("ARCHER_PRIVATE_HOST", @"^[a-z0-9][a-z0-9.-]+\.ts\.net$");
        var domain = Required("ARCHER_PRIVATE_AUTH0_DOMAIN", @"^[a-z0-9][a-z0-9.-]+\.auth0\.com$");
        var client = Required("ARCHER_PRIVATE_NATIVE_CLIENT_ID", @"^[a-zA-Z0-9_-]{12,128}$");
        var owner = Required("ARCHER_PRIVATE_OWNER_SUBJECT", @"^auth0\|[a-zA-Z0-9_-]{1,120}$");
        return new(host, Required("ARCHER_COMPANION_BRIDGE_KEY", @"^[a-fA-F0-9]{64}$"),
            Required("ARCHER_COMPANION_DEVICE_SHA256", @"^[a-fA-F0-9]{64}$").ToUpperInvariant(),
            new("https://" + domain + "/", "https://archer.example/api/dev", client, owner));
    }
}