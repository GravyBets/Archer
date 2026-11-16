using System.Net;
using System.Text;
using System.Text.Json;

namespace Archer.MissionControl.Services;

/// <summary>
/// OPTIONAL one-way local inbox sync to a DIFFERENT process on 127.0.0.1:5091.
/// Disabled without owner opt-in and a separate local ingestion key.
/// Never syncs admin endpoints, AI conversations, GPS, secrets, code proposals,
/// phone details, full mission artifacts, or write permissions.
/// </summary>
public sealed class LocalMobileGatewaySyncWorker(
    OperatorInboxService inbox,
    JsonFileStore store,
    IHttpClientFactory clients,
    ILogger<LocalMobileGatewaySyncWorker> logger) : BackgroundService
{
    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("ARCHER_GATEWAY_SYNC_ENABLED"),
            "true", StringComparison.Ordinal) &&
        (Environment.GetEnvironmentVariable("ARCHER_GATEWAY_INGEST_KEY")?.Length ?? 0) >= 40;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Enabled)
            {
                try
                {
                    await inbox.SyncAsync(ct);
                    var now = DateTimeOffset.UtcNow;
                    var events = await store.ReadAsync(state =>
                        state.Notifications
                            .Where(n => n.OccurredAtUtc >= now.AddDays(-30) &&
                                n.OccurredAtUtc <= now.AddMinutes(2))
                            .OrderByDescending(n => n.OccurredAtUtc)
                            .Take(40)
                            .Select(n => new
                            {
                                sourceKey = n.SourceKey,
                                category = n.Category,
                                priority = n.Priority,
                                title = n.Title,
                                summary = n.Summary,
                                occurredAtUtc = n.OccurredAtUtc,
                                resolvedAtUtc = n.ResolvedAtUtc
                            }).ToArray(), ct);

                    if (events.Length > 0)
                    {
                        using var request = new HttpRequestMessage(
                            HttpMethod.Post, "http://127.0.0.1:5091/internal/events");
                        request.Headers.Add("X-Archer-Ingest-Key",
                            Environment.GetEnvironmentVariable("ARCHER_GATEWAY_INGEST_KEY"));
                        request.Content = new StringContent(
                            JsonSerializer.Serialize(new { events }),
                            Encoding.UTF8, "application/json");
                        using var response = await clients.CreateClient("gateway-loopback")
                            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                        if (!response.IsSuccessStatusCode)
                            logger.LogWarning("Mobile gateway event sync rejected (HTTP {Code}).",
                                (int)response.StatusCode);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (HttpRequestException)
                {
                    // Gateway may be shut down; no retry storm or external fallback.
                    logger.LogDebug("Optional local mobile gateway isn't available.");
                }
                catch (Exception error)
                {
                    logger.LogWarning("Optional mobile gateway event sync skipped: {ErrorType}.",
                        error.GetType().Name);
                }
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
