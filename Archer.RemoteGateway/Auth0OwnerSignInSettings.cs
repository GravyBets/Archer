using System.Text.RegularExpressions;

namespace Archer.RemoteGateway;

/// <summary>
/// Explicit, development-only Auth0 OWNER sign-in verification configuration.
/// This proves a signed owner access token in a localhost no-data endpoint;
/// it does not authorize a phone, grant access, or register a device.
/// </summary>
internal sealed record Auth0OwnerSignInSettings(
    string Issuer, string Audience, string NativeClientId,
    string OwnerSubject)
{
    private const string Prefix = "ARCHER_REMOTE_AUTH0_SIGNIN_";

    internal static Auth0OwnerSignInSettings? FromEnvironment(
        bool isDevelopment, Func<string, string?> getenv)
    {
        string? Read(string key) => getenv(Prefix + key);
        var names = new[]
        {
            "ENABLED", "DOMAIN", "API_AUDIENCE",
            "NATIVE_CLIENT_ID", "OWNER_SUBJECT"
        };
        if (!names.Any(name => Read(name) is not null))
            return null;

        if (!isDevelopment ||
            !string.Equals(getenv("ARCHER_REMOTE_GATEWAY_MODE"),
                "local-test", StringComparison.Ordinal) ||
            !string.Equals(Read("ENABLED"), "local-proof",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Auth0 owner sign-in proof requires Development + local-test + explicit local-proof.");

        string Required(string key, int maxLength)
        {
            var value = Read(key);
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > maxLength ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value.Any(char.IsControl))
                throw new InvalidOperationException(
                    "Invalid or missing Auth0 sign-in proof configuration: " + key);
            return value;
        }

        var domain = Required("DOMAIN", 180);
        if (!Regex.IsMatch(domain,
                @"^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+\.auth0\.com$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100)))
            throw new InvalidOperationException(
                "Auth0 sign-in domain must be an approved Auth0 tenant hostname.");

        var clientId = Required("NATIVE_CLIENT_ID", 128);
        if (!Regex.IsMatch(clientId, @"^[A-Za-z0-9_-]{12,128}$",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidOperationException(
                "Invalid Native app client ID for Auth0 owner sign-in.");

        var ownerSubject = Required("OWNER_SUBJECT", 128);
        if (!ownerSubject.StartsWith("auth0|", StringComparison.Ordinal) ||
            ownerSubject.Length <= "auth0|".Length ||
            ownerSubject.Any(char.IsWhiteSpace))
            throw new InvalidOperationException(
                "Auth0 owner subject must be an exact database-connection sub (not an email address).");

        var audience = Required("API_AUDIENCE", 256);
        if (!string.Equals(audience, "https://archer.example/api/dev",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Auth0 owner-only proof audience must match Archer Remote Gateway Dev API.");

        return new Auth0OwnerSignInSettings(
            "https://" + domain.ToLowerInvariant() + "/",
            audience, clientId, ownerSubject);
    }
}
