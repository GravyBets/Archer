using System.Net;

namespace Archer.RemoteGateway;

/// <summary>
/// Temporary local boundary for the contract-only RemoteGateway skeleton.
/// This is NOT a production security component or a switch to enable public access.
/// Never replace the explicit Kestrel Listen binding with config-supplied URLs.
/// </summary>
internal static class SecurityBoundary
{
    internal const int Port = 5093;

    internal static void DemandLocalOnlyEnvironment()
    {
        var mode = Environment.GetEnvironmentVariable("ARCHER_REMOTE_GATEWAY_MODE");
        if (mode is not null && !string.Equals(mode, "local-test", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "RemoteGateway supports only local-test mode; remote access is not implemented.");

        var publicFlag = Environment.GetEnvironmentVariable("ARCHER_REMOTE_PUBLIC_ENABLED");
        if (!string.IsNullOrWhiteSpace(publicFlag) &&
            !string.Equals(publicFlag.Trim(), "false", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(publicFlag.Trim(), "0", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Refusing public gateway mode: production identity and TLS have not been implemented.");

        if (!string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("ARCHER_REMOTE_BIND_ADDRESS")))
            throw new InvalidOperationException(
                "Remote binding configuration is forbidden in this local-only skeleton.");
    }

    internal static bool AcceptLocalRequest(HttpContext context)
    {
        var request = context.Request;
        var host = request.Host;
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote) ||
            host.Port != Port ||
            !(string.Equals(host.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(host.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
            return false;

        // No browser origins, untrusted reverse proxies, forwarded authority
        // or public tunneling are accepted by this prototype.
        var headers = request.Headers;
        return !headers.ContainsKey("Origin") &&
            !headers.ContainsKey("Forwarded") &&
            !headers.ContainsKey("X-Forwarded-For") &&
            !headers.ContainsKey("X-Forwarded-Host") &&
            !headers.ContainsKey("X-Forwarded-Proto") &&
            !headers.ContainsKey("X-Real-IP");
    }
}
