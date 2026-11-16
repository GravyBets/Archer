using System.Text.Json;

namespace Archer.MissionControl.Services;

/// <summary>
/// Opt-in, READ-ONLY local preview of phone-authored test drafts from the
/// independent mobile gateway. No changes to desktop chat, no AI submission,
/// phone calls, credentials or public network access. Disabled by default.
/// </summary>
public sealed class GatewayConversationBridge(IHttpClientFactory clients)
{
    private const string Endpoint = "http://127.0.0.1:5091/internal/conversation/bridge";
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public sealed class DraftItem
    {
        public Guid Id { get; set; }
        public Guid DeviceId { get; set; }
        public string Content { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
    }

    private sealed class GatewayPayload
    {
        public List<DraftItem> Messages { get; set; } = [];
    }

    public bool IsEnabled =>
        Environment.GetEnvironmentVariable("ARCHER_GATEWAY_CHAT_BRIDGE_ENABLED") == "true" &&
        (Environment.GetEnvironmentVariable("ARCHER_GATEWAY_BRIDGE_KEY")?.Length ?? 0) >= 40;

    public async Task<object> PreviewAsync(CancellationToken ct)
    {
        if (!IsEnabled)
            return new
            {
                enabled = false,
                connected = false,
                messages = Array.Empty<DraftItem>(),
                note = "Phone draft preview is off. No messages or tokens are fetched."
            };

        var key = Environment.GetEnvironmentVariable("ARCHER_GATEWAY_BRIDGE_KEY")!;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Add("X-Archer-Bridge-Key", key);

            // Uses the dedicated proxy-free/redirect-free local HTTP client;
            // no arbitrary target or user-supplied gateway URL is accepted.
            using var response = await clients.CreateClient("gateway-loopback")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return new
                {
                    enabled = true,
                    connected = false,
                    messages = Array.Empty<DraftItem>(),
                    note = "Local mobile gateway rejected the read-only bridge. Check the independently configured bridge key and gateway readiness."
                };

            if (response.Content.Headers.ContentLength is > 128_000)
                throw new InvalidDataException("Bridge preview exceeded the expected maximum size.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            // Bounded read even if Content-Length is absent or falsified.
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int bytes;
            while ((bytes = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct)) > 0)
            {
                if (buffer.Length + bytes > 128_000)
                    throw new InvalidDataException("Bridge preview exceeded the expected maximum size.");
                buffer.Write(chunk, 0, bytes);
            }
            buffer.Position = 0;
            var payload = await JsonSerializer.DeserializeAsync<GatewayPayload>(buffer, Json, ct) ??
                throw new InvalidDataException("No bridge preview data was returned.");

            if (payload.Messages.Count > 30 ||
                payload.Messages.Any(x => x.Id == Guid.Empty || x.DeviceId == Guid.Empty ||
                    x.Content.Length is < 1 or > 1600))
                throw new InvalidDataException("Bridge preview contained invalid message metadata.");

            return new
            {
                enabled = true,
                connected = true,
                messages = payload.Messages,
                aiRequestsMade = 0,
                automaticallySubmitted = false,
                note = "Messages are previewed ONLY. Selecting a draft copies it into the composer; you must explicitly press Send to make any paid AI request."
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never reflect the bridge credential, phone drafts, local file
            // paths or raw HTTP errors into the browser.
            return new
            {
                enabled = true,
                connected = false,
                messages = Array.Empty<DraftItem>(),
                note = "Read-only bridge unavailable. No message was sent. Check that the local gateway is running with its encryption and bridge secrets configured."
            };
        }
    }
}
