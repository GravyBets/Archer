using System.Net;
using System.Text.RegularExpressions;

namespace Archer.RemoteGateway;

/// <summary>
/// Auth0-specific, local-only trust configuration. Neither this record nor the
/// caller registers a public endpoint or creates an Auth0 tenant. A configured
/// Auth0 "Native" client and API must be reviewed separately before testing.
/// </summary>
internal sealed record Auth0LocalProfile(
    OidcProviderSettings Oidc,
    string NativeClientId,
    string DeviceClaimType)
{
    private const string Prefix = "ARCHER_REMOTE_AUTH0_";
    private const string ExplicitMode = "local-validation";

    internal static Auth0LocalProfile? FromEnvironment(
        bool isDevelopment, Func<string, string?> getEnvironment)
    {
        string? Read(string name) => getEnvironment(Prefix + name);

        var configuredNames = new[]
        {
            "ENABLED", "DOMAIN", "API_AUDIENCE", "NATIVE_CLIENT_ID",
            "OWNER_SUBJECT", "DEVICE_REGISTRY_FILE", "DEVICE_CLAIM_TYPE"
        };
        if (!configuredNames.Any(name => Read(name) is not null))
            return null;

        if (!isDevelopment ||
            !string.Equals(getEnvironment("ARCHER_REMOTE_GATEWAY_MODE"),
                "local-test", StringComparison.Ordinal) ||
            !string.Equals(Read("ENABLED"), ExplicitMode,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Auth0 profile requires explicit Development + local-test + local-validation.");

        string Required(string name, int max)
        {
            var value = Read(name);
            if (string.IsNullOrWhiteSpace(value) || value.Length > max ||
                value != value.Trim() || value.Any(char.IsControl))
                throw new InvalidOperationException(
                    "Missing or invalid Auth0 local profile setting: " + name);
            return value;
        }

        // Use the canonical Auth0 tenant domain only until a separately
        // reviewed custom-domain/TLS policy exists. Never follow a caller URL.
        var domain = Required("DOMAIN", 180);
        if (!Regex.IsMatch(
                domain,
                @"^[a-z0-9][a-z0-9-]*(\.[a-z0-9][a-z0-9-]*)+\.auth0\.com$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100)) ||
            domain.Contains("..", StringComparison.Ordinal) ||
            domain.StartsWith("auth0.", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(domain, out _))
            throw new InvalidOperationException(
                "Auth0 Domain must be the tenant's DNS hostname ending in .auth0.com (no scheme, path or port).");

        var clientId = Required("NATIVE_CLIENT_ID", 128);
        if (!Regex.IsMatch(clientId, @"^[A-Za-z0-9_-]{12,128}$",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidOperationException("Invalid Auth0 Native application Client ID.");

        // Access-token device claim MUST be namespaced (Auth0 Actions).
        // A device claim in a signed JWT is not device possession: the
        // pairing key still needs separate sender-constraining design.
        var claim = Required("DEVICE_CLAIM_TYPE", 512);
        if (!Uri.TryCreate(claim, UriKind.Absolute, out var claimUri) ||
            claimUri.Scheme != Uri.UriSchemeHttps ||
            claimUri.HostNameType != UriHostNameType.Dns ||
            claimUri.Port != 443 ||
            claimUri.UserInfo.Length != 0 ||
            claimUri.Query.Length != 0 || claimUri.Fragment.Length != 0 ||
            !claimUri.AbsolutePath.EndsWith("/device_id", StringComparison.Ordinal) ||
            claimUri.AbsolutePath.Length <= "/device_id".Length ||
            claimUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            claimUri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            claimUri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            claimUri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            claimUri.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            claimUri.Host.Equals("auth0.com", StringComparison.OrdinalIgnoreCase) ||
            claimUri.Host.EndsWith(".auth0.com", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(claimUri.Host, out _))
            throw new InvalidOperationException(
                "Auth0 device claim must be a namespaced HTTPS URI under a domain you control, ending in /device_id.");

        var audience = Required("API_AUDIENCE", 256);
        var owner = Required("OWNER_SUBJECT", 128);
        var registry = Required("DEVICE_REGISTRY_FILE", 1024);

        // Reuse our fail-closed generic OIDC validation; map static, not
        // request-provided, values. Auth0 issuer ALWAYS ends in '/'.
        var config = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ARCHER_REMOTE_GATEWAY_MODE"] = getEnvironment("ARCHER_REMOTE_GATEWAY_MODE"),
            ["ARCHER_REMOTE_OIDC_ENABLED"] = "local-validation",
            ["ARCHER_REMOTE_OIDC_ISSUER"] = "https://" + domain.ToLowerInvariant() + "/",
            ["ARCHER_REMOTE_OIDC_AUDIENCE"] = audience,
            ["ARCHER_REMOTE_OIDC_OWNER_SUBJECT"] = owner,
            ["ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE"] = registry
        };
        var oidc = OidcProviderSettings.FromEnvironment(
            isDevelopment, key => config.GetValueOrDefault(key))
            ?? throw new InvalidOperationException(
                "Auth0 OIDC verification configuration could not be constructed.");

        return new Auth0LocalProfile(oidc, clientId, claim);
    }
}
