using System.Security.Claims;

namespace Archer.RemoteGateway;

/// <summary>
/// Pure synthetic testable claim gate. The caller MUST already have validated
/// the JWT issuer, cryptographic signature, audience, algorithm and expiry
/// using official JWT bearer middleware. This class alone cannot authenticate.
/// </summary>
internal static class Auth0OwnerClaimPolicy
{
    internal static bool AcceptValidatedClaims(
        ClaimsPrincipal? validatedPrincipal, Auth0OwnerSignInSettings config)
    {
        var subject = ExactlyOne(validatedPrincipal, "sub");
        var authorizedParty = ExactlyOne(validatedPrincipal, "azp");
        var oauthScope = ExactlyOne(validatedPrincipal, "scope");
        var delegatedScope = ExactlyOne(validatedPrincipal, "scp");
        // A signed token must name exactly one server-pinned public OAuth
        // client. Native and Pocket SPA are separate registrations; adding
        // Pocket is opt-in and never inferred from an arbitrary `azp`.
        var knownClient = authorizedParty is not null &&
            (string.Equals(authorizedParty, config.NativeClientId,
                StringComparison.Ordinal) ||
             (config.PocketSpaClientId is not null &&
              string.Equals(authorizedParty, config.PocketSpaClientId,
                  StringComparison.Ordinal)));
        if (subject is null || !knownClient ||
            !string.Equals(subject, config.OwnerSubject, StringComparison.Ordinal) ||
            (oauthScope is null) == (delegatedScope is null))
            return false;

        var scopeText = oauthScope ?? delegatedScope ?? "";
        var scopes = scopeText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (scopes.Length is 0 or > 16 ||
            scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Length)
            return false;

        return scopes.Contains("conversation:read", StringComparer.Ordinal);
    }

    private static string? ExactlyOne(ClaimsPrincipal? principal, string type)
    {
        var found = principal?.FindAll(type).ToArray();
        return found is { Length: 1 } && !string.IsNullOrWhiteSpace(found[0].Value)
            ? found[0].Value
            : null;
    }
}
