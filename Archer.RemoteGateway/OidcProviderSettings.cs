using System.Net;

namespace Archer.RemoteGateway;

/// <summary>
/// Validates an explicit, allowlisted OIDC issuer configuration for a LOCAL
/// ONLY API validation probe. This is not a complete native iOS OAuth client
/// or a production-approval switch. Nothing here opens a public listener.
/// </summary>
internal sealed record OidcProviderSettings(
    string Issuer,
    string Audience,
    string OwnerSubject,
    string RegistryFile)
{
    private const string Prefix = "ARCHER_REMOTE_OIDC_";
    private const string ExplicitEnableValue = "local-validation";

    internal static OidcProviderSettings? FromEnvironment(
        bool isDevelopment, Func<string, string?> getEnvironment)
    {
        string? Value(string key) => getEnvironment(Prefix + key);
        var names = new[] {
            "ENABLED", "ISSUER", "AUDIENCE", "OWNER_SUBJECT", "DEVICE_REGISTRY_FILE"
        };
        if (!names.Any(name => Value(name) is not null))
            return null;

        if (!isDevelopment ||
            !string.Equals(getEnvironment("ARCHER_REMOTE_GATEWAY_MODE"),
                "local-test", StringComparison.Ordinal) ||
            !string.Equals(Value("ENABLED"), ExplicitEnableValue,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "OIDC verification is local-test/Development only and requires explicit local-validation opt-in.");

        string Required(string name, int maxLength)
        {
            var value = Value(name);
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > maxLength ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value.Any(char.IsControl))
                throw new InvalidOperationException(
                    "Incomplete or invalid local OIDC provider configuration: " + name);
            return value;
        }

        var issuer = Required("ISSUER", 512);
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.HostNameType != UriHostNameType.Dns ||
            uri.Port != 443 || // No arbitrary test or internal service ports.
            uri.UserInfo.Length != 0 ||
            uri.Fragment.Length != 0 || uri.Query.Length != 0 ||
            uri.Host.Length < 4 || !uri.Host.Contains('.') ||
            uri.Host.StartsWith('.', StringComparison.Ordinal) ||
            uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(uri.Host, out _))
            throw new InvalidOperationException(
                "OIDC issuer must be an approved public HTTPS DNS name on port 443, without query or fragment.");

        var audience = Required("AUDIENCE", 256);
        var owner = Required("OWNER_SUBJECT", 128);
        var registry = Required("DEVICE_REGISTRY_FILE", 1024);
        if (!Path.IsPathFullyQualified(registry) || !File.Exists(registry))
            throw new InvalidOperationException(
                "An existing absolute synthetic enrollment registry is required for OIDC validation.");

        return new OidcProviderSettings(issuer, audience, owner, registry);
    }
}
