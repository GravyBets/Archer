using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Opt-in local availability checks for explicitly allowlisted PUBLIC HTTPS hosts.
/// No credentials, arbitrary URLs, redirect-following, SMS, phone calls or AI requests.
/// </summary>
public sealed class WebsiteMonitorWorker(
    JsonFileStore store,
    IHttpClientFactory clients,
    ILogger<WebsiteMonitorWorker> logger) : BackgroundService
{
    public static Uri ValidateMonitorUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.IdnHost) && Uri.CheckHostName(uri.IdnHost) != UriHostNameType.Dns ||
            uri.AbsolutePath.Length > 180 ||
            uri.ToString().Length > 350)
            throw new ArgumentException("Use a public HTTPS URL on port 443, with no credentials, query string or fragment.");

        var host = uri.DnsSafeHost;
        if (Uri.CheckHostName(host) != UriHostNameType.Dns ||
            !host.Contains('.') ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Site monitoring only supports public DNS hostnames.");

        // Explicit per-host permission must be supplied outside the app's source code.
        var approved = (Environment.GetEnvironmentVariable("ARCHER_MONITOR_HOSTS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!approved.Contains(host, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Host {host} is not approved. Add the exact hostname to your ARCHER_MONITOR_HOSTS environment variable and restart Archer.");
        return uri;
    }

    private static bool IsPublicIp(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var ip = address.GetAddressBytes();
            if (ip[0] is 0 or 10 or 127 or >= 224 ||
                ip[0] == 169 && ip[1] == 254 ||
                ip[0] == 172 && ip[1] is >= 16 and <= 31 ||
                ip[0] == 192 && ip[1] == 168 ||
                ip[0] == 100 && ip[1] is >= 64 and <= 127 ||
                ip[0] == 192 && ip[1] == 0 ||
                ip[0] == 198 && ip[1] is 18 or 19)
                return false;
            return true;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var ip = address.GetAddressBytes();
            return !address.IsIPv6LinkLocal && !address.IsIPv6Multicast &&
                !address.IsIPv6SiteLocal && !address.Equals(IPAddress.IPv6Any) &&
                !address.Equals(IPAddress.IPv6None) &&
                (ip[0] & 0xFE) != 0xFC && // Unique-local fc00::/7.
                (ip[0] != 0xFE || (ip[1] & 0xC0) != 0x80); // Link-local fe80::/10.
        }
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var monitors = await store.ReadAsync(state =>
                    state.WebsiteMonitors.Where(x => x.Enabled)
                        .Select(x => (x.Id, x.Url)).ToArray(), ct);

                foreach (var monitor in monitors)
                {
                    ct.ThrowIfCancellationRequested();
                    await CheckAsync(monitor.Id, monitor.Url, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError(error, "Website health monitoring cycle failed.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(5), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    private async Task CheckAsync(Guid monitorId, string url, CancellationToken ct)
    {
        var succeeded = false;
        int? statusCode = null;
        long? latency = null;
        string? errorMessage = null;
        try
        {
            var uri = ValidateMonitorUrl(url);
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
            if (addresses.Length == 0 || !addresses.All(IsPublicIp))
                throw new InvalidOperationException("Monitoring blocked: DNS resolved to a private or reserved address.");

            var watch = Stopwatch.StartNew();
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await clients.CreateClient("site-health").SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, ct);
            watch.Stop();
            statusCode = (int)response.StatusCode;
            latency = watch.ElapsedMilliseconds;
            succeeded = statusCode is >= 200 and < 400;
            if (!succeeded) errorMessage = $"HTTP {statusCode}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            errorMessage = error is HttpRequestException or TaskCanceledException
                ? "Site request failed or timed out."
                : error.Message;
        }

        await store.UpdateAsync(state =>
        {
            var monitor = state.WebsiteMonitors.FirstOrDefault(x => x.Id == monitorId && x.Enabled);
            if (monitor is null) return false;
            var oldStatus = monitor.Status;
            monitor.LastCheckedAtUtc = DateTimeOffset.UtcNow;
            monitor.LastHttpStatus = statusCode;
            monitor.LastLatencyMs = latency;
            monitor.LastError = errorMessage;

            if (succeeded)
            {
                monitor.Status = "Up";
                monitor.ConsecutiveFailures = 0;
                if (oldStatus == "Down")
                    LogIncident(state, monitor, "Recovered",
                        $"{monitor.Name} is responding to HTTPS availability checks again.");
            }
            else
            {
                monitor.ConsecutiveFailures++;
                monitor.Status = monitor.ConsecutiveFailures >= 3 ? "Down" : "Degraded";
                if (monitor.Status == "Down" && oldStatus != "Down")
                    LogIncident(state, monitor, "Outage",
                        $"{monitor.Name} failed three consecutive HTTPS checks. Check the website before taking action.");
            }
            return true;
        }, ct);
    }

    private static void LogIncident(ArcherState state, WebsiteMonitor monitor, string kind, string message)
    {
        state.WebsiteIncidents.Insert(0, new WebsiteIncident
        {
            WebsiteMonitorId = monitor.Id,
            WebsiteName = monitor.Name,
            Kind = kind,
            Message = message
        });
        if (state.WebsiteIncidents.Count > 200)
            state.WebsiteIncidents.RemoveRange(200, state.WebsiteIncidents.Count - 200);
    }
}
