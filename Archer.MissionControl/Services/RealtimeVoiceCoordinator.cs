using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Archer.MissionControl.Services;

/// <summary>
/// Opt-in localhost ONLY WebRTC offer broker. No browser ever receives the
/// long-lived provider key. This is a separate, billable real-time model
/// integration: NOT covered by the Responses API cost ledger. It is disabled
/// by default, bounded by a conservative durable initiation quota, and
/// requires a dedicated OpenAI project key with provider-side spending limits.
/// The 4-minute browser timer is a convenience, NOT a hard provider cost cap.
/// No transcripts, audio, or recordings are stored by this service.
/// </summary>
public sealed class RealtimeVoiceCoordinator
{
    private const string ApiHost = "https://api.openai.com/v1/realtime/calls";
    private const string OwnerOrigin = "http://127.0.0.1:5087";
    private const string OwnerHost = "127.0.0.1:5087";
    private const int DailyStartLimit = 3;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _guard = new(1, 1);
    private readonly string _counterFile;
    private readonly string _safetyIdFile;

    private sealed record StartCount(DateOnly UtcDay, int Count);

    public RealtimeVoiceCoordinator(IHttpClientFactory factory, IWebHostEnvironment env)
    {
        _http = factory.CreateClient("openai-realtime");
        var directory = Environment.GetEnvironmentVariable("ARCHER_DATA_DIR");
        directory = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(env.ContentRootPath, "Data")
            : Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        _counterFile = Path.Combine(directory, "realtime-voice-starts.json");
        _safetyIdFile = Path.Combine(directory, "realtime-safety-id.txt");
    }

    private static string? ProviderKey() =>
        Environment.GetEnvironmentVariable("ARCHER_REALTIME_API_KEY");

    public bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("ARCHER_REALTIME_VOICE_ENABLED"),
            "true", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ProviderKey());

    public object Status() => new
    {
        enabled = Enabled,
        mode = "local-webrtc",
        model = "gpt-realtime-2.1-mini",
        maxStartsPerUtcDay = DailyStartLimit,
        recommendedMaxSessionSeconds = 240,
        hardSpendCap = false,
        note = Enabled
            ? "Billable Realtime audio is enabled with a dedicated API key. This is independent of Archer's normal chat cost ledger. A session timer is not a guaranteed server-side cutoff."
            : "Realtime voice requires explicit opt-in and a separately budgeted API project key. Browser hands-free remains available."
    };

    public async Task<IResult> StartAsync(HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Enabled)
            return Results.Json(new { error = "Realtime voice is disabled. Use browser hands-free." },
                statusCode: 403);

        // Browser's cross-site application/sdp would need preflight; this app
        // does not enable CORS. Validate Origin/Host explicitly as well.
        if (!string.Equals(context.Request.Host.Value, OwnerHost, StringComparison.Ordinal)
            || !string.Equals(context.Request.Headers.Origin, OwnerOrigin,
                StringComparison.Ordinal))
            return Results.StatusCode(403);

        if (!string.Equals(context.Request.ContentType, "application/sdp",
                StringComparison.OrdinalIgnoreCase))
            return Results.StatusCode(415);
        if (context.Request.ContentLength is > 16384 or <= 0)
            return Results.StatusCode(413);
        string offer;
        using (var reader = new StreamReader(context.Request.Body,
                   Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                   bufferSize: 4096, leaveOpen: true))
        {
            var chars = new char[16385];
            var read = 0;
            while (read < chars.Length)
            {
                var next = await reader.ReadAsync(chars.AsMemory(read,
                    chars.Length - read), ct);
                if (next == 0) break;
                read += next;
            }
            if (read > 16384) return Results.StatusCode(413);
            offer = new string(chars, 0, read);
        }
        if (offer.Length < 20 || !(offer.StartsWith("v=0\r\n", StringComparison.Ordinal) ||
                offer.StartsWith("v=0\n", StringComparison.Ordinal))
            || !(offer.Contains("\nm=audio ", StringComparison.Ordinal) ||
                offer.Contains("\r\nm=audio ", StringComparison.Ordinal))
            || !offer.Contains("a=fingerprint:", StringComparison.Ordinal))
            return Results.BadRequest(new { error = "Invalid WebRTC audio offer." });

        // Keep provider usage limited even across application restarts and
        // concurrent requests. A failed provider attempt consumes a slot.
        if (!await ReserveStartAsync(ct))
            return Results.Json(new {
                error = "Realtime voice daily start limit reached or local usage history unreadable. No model call was made."
            }, statusCode: 429);

        var key = ProviderKey()!;
        var session = new
        {
            type = "realtime",
            model = "gpt-realtime-2.1-mini",
            instructions =
                "You are Archer in a private one-owner conversation. Respond like a natural, succinct, grounded British bloke, never a report. " +
                "Generally give one to three short spoken sentences, ask at most one question, allow thoughtful pauses, and let the owner interrupt you. " +
                "Only use list_local_missions and propose_local_mission for explicit owner requests. " +
                "These tools are limited to reading local mission titles/status and PROPOSING a draft for explicit on-screen confirmation. " +
                "A spoken yes never authorizes creating, planning, running, publishing, deploying or spending. " +
                "If the browser owner declines, say it was not created. You cannot run tasks, edit code, or make external calls. " +
                "Tool results are local-only and may be unavailable. Audio is sent to the model provider; do not solicit secrets.",
            tool_choice = "auto",
            tools = new object[]
            {
                new
                {
                    type = "function",
                    name = "list_local_missions",
                    description = "Read up to eight local mission titles, statuses and task counts. No external access or mutations.",
                    parameters = new { type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false }
                },
                new
                {
                    type = "function",
                    name = "propose_local_mission",
                    description = "Show the owner a local on-screen confirmation to CREATE A DRAFT mission ONLY. A voice answer never counts as consent. Does not plan or execute.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string", description = "Short mission title (3 to 100 characters)" },
                            objective = new { type = "string", description = "Mission objective (10 to 500 characters)" }
                        },
                        required = new [] { "title", "objective" },
                        additionalProperties = false
                    }
                }
            },
            audio = new
            {
                input = new
                {
                    turn_detection = new
                    {
                        type = "semantic_vad",
                        eagerness = "low",
                        create_response = true,
                        interrupt_response = true
                    }
                },
                output = new { voice = "cedar" }
            }
        };
        try
        {
            using var form = new MultipartFormDataContent();
            var offerPart = new StringContent(offer, Encoding.UTF8);
            offerPart.Headers.ContentType = new MediaTypeHeaderValue("application/sdp");
            form.Add(offerPart, "sdp");
            var sessionPart = new StringContent(JsonSerializer.Serialize(session), Encoding.UTF8);
            sessionPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            form.Add(sessionPart, "session");

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiHost);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.TryAddWithoutValidation("OpenAI-Safety-Identifier", GetOrCreateSafetyId());
            request.Content = form;
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return Results.Json(new {
                    error = response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "Realtime provider rate or spending limit reached."
                        : "Realtime provider rejected the session. Check model access, project credits and usage limits."
                }, statusCode: 502);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (body.Length is < 20 or > 32768 ||
                !(body.StartsWith("v=0\r\n", StringComparison.Ordinal) ||
                  body.StartsWith("v=0\n", StringComparison.Ordinal)))
                return Results.Json(new { error = "Realtime provider returned an invalid SDP answer." },
                    statusCode: 502);
            return Results.Text(body, "application/sdp");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HttpRequestException)
        {
            return Results.Json(new { error = "Realtime provider network connection failed." },
                statusCode: 502);
        }
        catch (TaskCanceledException)
        {
            return Results.Json(new { error = "Realtime connection timed out." },
                statusCode: 504);
        }
    }

    private async Task<bool> ReserveStartAsync(CancellationToken ct)
    {
        await _guard.WaitAsync(ct);
        try
        {
            var now = DateOnly.FromDateTime(DateTime.UtcNow);
            StartCount counter = new(now, 0);
            if (File.Exists(_counterFile))
            {
                try
                {
                    // Fail closed on corrupt or tampered usage history.
                    counter = JsonSerializer.Deserialize<StartCount>(
                        await File.ReadAllTextAsync(_counterFile, ct))
                        ?? throw new InvalidDataException("Missing quota state.");
                    if (counter.Count is < 0 or > 10000 || counter.UtcDay > now)
                        return false;
                }
                catch (Exception e) when (e is JsonException or IOException or InvalidDataException)
                {
                    return false;
                }
            }
            if (counter.UtcDay != now) counter = new StartCount(now, 0);
            if (counter.Count >= DailyStartLimit) return false;
            var next = counter with { Count = counter.Count + 1 };
            var temp = _counterFile + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(next), ct);
            File.Move(temp, _counterFile, overwrite: true);
            return true;
        }
        finally { _guard.Release(); }
    }

    private string GetOrCreateSafetyId()
    {
        lock (_safetyIdFile)
        {
            if (File.Exists(_safetyIdFile))
            {
                var current = File.ReadAllText(_safetyIdFile).Trim();
                if (current.Length == 64 &&
                    current.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                    return current;
                // Fail closed rather than regenerating silently after corruption.
                throw new InvalidDataException("Realtime safety identifier invalid.");
            }
            var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            File.WriteAllText(_safetyIdFile, value);
            return value;
        }
    }
}
