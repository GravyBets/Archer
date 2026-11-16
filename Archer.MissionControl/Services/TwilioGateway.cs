using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Archer.MissionControl.Services;

/// <summary>
/// Single-recipient Twilio adapter. Disabled without explicit environment
/// consent; never accepts an arbitrary phone number from a model or HTTP request.
/// </summary>
public sealed class TwilioGateway(HttpClient client)
{
    private static readonly Regex Phone = new(@"^\+[1-9][0-9]{7,14}$", RegexOptions.Compiled);
    private static readonly Regex Sid = new(@"^AC[0-9a-fA-F]{32}$", RegexOptions.Compiled);

    private string AccountSid => Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID") ?? "";
    private string AuthToken => Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN") ?? "";
    private string From => Environment.GetEnvironmentVariable("ARCHER_TWILIO_FROM") ?? "";
    private string To => Environment.GetEnvironmentVariable("ARCHER_TWILIO_TO") ?? "";

    public bool IsConfigured =>
        Environment.GetEnvironmentVariable("ARCHER_TWILIO_ENABLED") == "true" &&
        Sid.IsMatch(AccountSid) &&
        !string.IsNullOrWhiteSpace(AuthToken) &&
        Phone.IsMatch(From) && Phone.IsMatch(To);

    public string RecipientLabel => !Phone.IsMatch(To) ? "Not configured" : "***" + To[^4..];

    public async Task<string> SendSmsAsync(string text, CancellationToken ct)
    {
        CheckConfiguration();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 600)
            throw new ArgumentException("A single SMS notification must be 1–600 characters.");

        return await PostFormAsync("Messages", new Dictionary<string, string>
        {
            ["To"] = To,
            ["From"] = From,
            ["Body"] = text
        }, ct);
    }

    public async Task<string> PlaceCallAsync(string speech, CancellationToken ct)
    {
        CheckConfiguration();
        if (string.IsNullOrWhiteSpace(speech) || speech.Length > 500)
            throw new ArgumentException("A voice announcement must be 1–500 characters.");

        // This en-GB male voice is explicitly listed by Twilio. Quality,
        // third-party TTS charges and availability vary by Twilio account.
        // XML-escape anything derived from an incident/website name.
        var escaped = SecurityElement.Escape(speech) ?? "";
        var twiml = "<Response><Say voice=\"Polly.Brian-Neural\" language=\"en-GB\">" +
            escaped + "</Say><Hangup/></Response>";
        return await PostFormAsync("Calls", new Dictionary<string, string>
        {
            ["To"] = To,
            ["From"] = From,
            ["Twiml"] = twiml,
            ["Timeout"] = "20",
            ["TimeLimit"] = "60"
        }, ct);
    }

    private void CheckConfiguration()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Twilio delivery is disabled or incomplete. Configure explicit environment settings before attempting messages.");
    }

    private async Task<string> PostFormAsync(
        string resource, IReadOnlyDictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/{resource}.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(AccountSid + ":" + AuthToken)));
        request.Content = new FormUrlEncodedContent(form);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Never print the provider's error body: it can contain phone details.
            throw new InvalidOperationException(
                $"Twilio {resource} request was not accepted (HTTP {(int)response.StatusCode}). " +
                "Check verified numbers, consent, permissions, credits and provider logs.");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var sid = json.RootElement.TryGetProperty("sid", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(sid) || sid.Length is < 20 or > 60)
            throw new InvalidOperationException("Twilio returned no valid request identifier. Check your provider dashboard.");
        return sid;
    }
}
