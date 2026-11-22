using System.Security.Claims;

namespace Archer.PrivateGateway;

/// <summary>
/// A validated owner JWT is necessary but not sufficient: requesting an
/// overview requires read scope, while message submissions and receipt lookup
/// require write scope. Neither scope authorizes admin routes.
/// </summary>
internal static class GatewayScopes
{
    internal static bool Allows(ClaimsPrincipal? user, string required)
    {
        if (user?.Identity?.IsAuthenticated != true ||
            required is not ("conversation:read" or "conversation:write"))
            return false;
        return user.Claims
            .Where(c => c.Type is "scope" or "scp")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(required, StringComparer.Ordinal);
    }
}
