using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Consent-gated notification dispatch. Durable attempt reservations prevent
/// repeated calls/messages after process restarts or ambiguous provider errors.
/// </summary>
public sealed class CommunicationCoordinator(
    JsonFileStore store, BusinessBriefingComposer composer, TwilioGateway gateway)
{
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(20);

    public async Task<object> GetStatusAsync(CancellationToken ct)
    {
        return await store.ReadAsync(state => new
        {
            providerConfigured = gateway.IsConfigured,
            recipient = gateway.RecipientLabel,
            preferences = state.CommunicationPreferences,
            attempts = state.CommunicationAttempts.Take(30).ToArray(),
            dailyMaxSms = 3,
            dailyMaxCalls = 1,
            warning = "Twilio delivery can incur separate SMS, calling and neural TTS charges. Provider acceptance is not confirmed phone delivery. No arbitrary recipient is accepted."
        }, ct);
    }

    public async Task<object> SavePreferencesAsync(
        UpdateCommunicationPreferencesRequest request, CancellationToken ct)
    {
        if (!request.ConfirmCharges)
            throw new ArgumentException("Confirm you consent to charges before enabling notifications.");
        if ((request.DailySmsEnabled || request.UrgentCallsEnabled) && !gateway.IsConfigured)
            throw new InvalidOperationException("Configure your private Twilio credentials and verified destination before enabling delivery.");

        await store.UpdateAsync(state =>
        {
            state.CommunicationPreferences = new CommunicationPreferences
            {
                DailySmsEnabled = request.DailySmsEnabled,
                UrgentCallsEnabled = request.UrgentCallsEnabled
            };
            return true;
        }, ct);
        return new
        {
            message = "Delivery preferences saved. Notifications run only while Archer is running and Twilio remains configured.",
            dailySmsEnabled = request.DailySmsEnabled,
            urgentCallsEnabled = request.UrgentCallsEnabled
        };
    }

    private async Task<CommunicationAttempt?> ReserveAsync(
        string kind, string channel, string key, CancellationToken ct)
    {
        if (!gateway.IsConfigured)
            throw new InvalidOperationException("Twilio provider is not configured or enabled.");

        return await store.UpdateAsync<CommunicationAttempt?>(state =>
        {
            if (state.CommunicationAttempts.Any(x => x.DeduplicationKey == key))
                return null; // Never retry ambiguous provider attempts without an owner intervention.

            var todayUtc = DateTimeOffset.UtcNow.Date;
            var count = state.CommunicationAttempts.Count(x =>
                x.Channel == channel && x.CreatedAtUtc.UtcDateTime.Date == todayUtc);
            if (count >= (channel == "Call" ? 1 : 3))
                return null;

            var attempt = new CommunicationAttempt
            {
                Kind = kind, Channel = channel, DeduplicationKey = key
            };
            state.CommunicationAttempts.Insert(0, attempt);
            if (state.CommunicationAttempts.Count > 200)
                state.CommunicationAttempts.RemoveRange(200, state.CommunicationAttempts.Count - 200);
            return attempt;
        }, ct);
    }

    private async Task MarkAsync(Guid id, string status, string? providerReference)
    {
        await store.UpdateAsync(state =>
        {
            var attempt = state.CommunicationAttempts.FirstOrDefault(x => x.Id == id);
            if (attempt is not null)
            {
                attempt.Status = status;
                attempt.ProviderReference = providerReference;
            }
            return true;
        }, CancellationToken.None);
    }

    private async Task<string> SendSmsAsync(
        string message, string kind, string key, CancellationToken ct)
    {
        var reservation = await ReserveAsync(kind, "SMS", key, ct);
        if (reservation is null)
            throw new InvalidOperationException(
                "Message suppressed: duplicate request or the three-SMS daily safety limit was reached.");
        try
        {
            var sid = await gateway.SendSmsAsync(message, ct);
            await MarkAsync(reservation.Id, "Submitted", sid);
            return sid;
        }
        catch
        {
            await MarkAsync(reservation.Id, "Uncertain", null);
            throw; // Do not automatically retry; the provider might have accepted the request.
        }
    }

    private async Task<string> PlaceCallAsync(
        string speech, string kind, string key, CancellationToken ct)
    {
        var reservation = await ReserveAsync(kind, "Call", key, ct);
        if (reservation is null)
            throw new InvalidOperationException(
                "Call suppressed: duplicate request or the one-call daily safety limit was reached.");
        try
        {
            var sid = await gateway.PlaceCallAsync(speech, ct);
            await MarkAsync(reservation.Id, "Submitted", sid);
            return sid;
        }
        catch
        {
            await MarkAsync(reservation.Id, "Uncertain", null);
            throw;
        }
    }

    public async Task<string> ManualSmsTestAsync(CancellationToken ct)
    {
        var text = "Archer test: text delivery is connected. No automatic notifications " +
                   "are enabled by sending this message. Check Operations to opt in.";
        return await SendSmsAsync(text, "Manual test", "manual-sms:" + Guid.NewGuid().ToString("N"), ct);
    }

    public async Task<string> ManualCallTestAsync(CancellationToken ct)
    {
        var speech = "Hello. It's Archer. This is a test of your British voice call alerts. " +
                     "You're in control of which notifications I send. Goodbye.";
        return await PlaceCallAsync(speech, "Manual test", "manual-call:" + Guid.NewGuid().ToString("N"), ct);
    }

    private static bool IsQuietHour(BriefingPreferences p, int hour)
    {
        if (p.QuietStartHourLocal == p.QuietEndHourLocal) return false;
        return p.QuietStartHourLocal < p.QuietEndHourLocal
            ? hour >= p.QuietStartHourLocal && hour < p.QuietEndHourLocal
            : hour >= p.QuietStartHourLocal || hour < p.QuietEndHourLocal;
    }

    public async Task RunScheduledCheckAsync(CancellationToken ct)
    {
        if (!gateway.IsConfigured) return;
        var snapshot = await store.ReadAsync(state => new
        {
            preferences = state.CommunicationPreferences,
            briefingPreferences = state.BriefingPreferences,
            incidents = state.WebsiteIncidents
                .Where(i => i.Kind == "Outage" && i.AcknowledgedAtUtc is null)
                .Take(10).ToArray(),
            monitors = state.WebsiteMonitors.ToArray()
        }, ct);
        var now = DateTimeOffset.UtcNow;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(snapshot.briefingPreferences.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, tz);
        var quiet = IsQuietHour(snapshot.briefingPreferences, local.Hour);

        if (snapshot.preferences.DailySmsEnabled &&
            local.Hour == snapshot.briefingPreferences.DailyHourLocal &&
            local.Minute is >= 0 and < 5 && !quiet)
        {
            var report = await composer.BuildAsync(ct);
            var body = report.BriefingText.Replace("\r", "").Replace("\n", " ");
            if (body.Length > 570) body = body[..566] + "…";
            // A single daily text is reserved BEFORE a provider call.
            var key = "daily:" + local.ToString("yyyy-MM-dd") + ":" + snapshot.briefingPreferences.TimeZoneId;
            try { await SendSmsAsync(body, "Daily briefing", key, ct); }
            catch (InvalidOperationException) { /* duplicate or daily safety limit */ }
        }

        if (!snapshot.preferences.UrgentCallsEnabled ||
            quiet && !snapshot.briefingPreferences.CriticalOverrideQuietHours)
            return;

        // Outage alerts: only after three independent failed checks, and only
        // while the latest check is current. This is not payment/transaction monitoring.
        foreach (var incident in snapshot.incidents)
        {
            var site = snapshot.monitors.FirstOrDefault(x => x.Id == incident.WebsiteMonitorId);
            if (site is null || !site.Enabled || site.Status != "Down" ||
                site.ConsecutiveFailures < 3 ||
                site.LastCheckedAtUtc is not { } checkedAt ||
                checkedAt < now.Subtract(Freshness))
                continue;
            var speech = "Archer here. Your website " + site.Name +
                " has failed three consecutive availability checks. " +
                "I can't confirm a full outage, so please review the Operations dashboard. " +
                "No changes have been made.";
            if (speech.Length > 500) speech = speech[..497] + "...";
            try
            {
                await PlaceCallAsync(speech, "Verified monitoring threshold",
                    "outage:" + incident.Id.ToString("N"), ct);
            }
            catch (InvalidOperationException) { /* duplicate or daily safety limit */ }
            break; // At most one incident attempted per polling cycle.
        }
    }
}

/// <summary>
/// Delivery is opt-in and bounded; never reconnects to a phone automatically
/// when the Archer PC process is stopped.
/// </summary>
public sealed class CommunicationWorker(
    CommunicationCoordinator coordinator,
    ILogger<CommunicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await coordinator.RunScheduledCheckAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning(error,
                    "The communication check did not complete. No immediate retry will be attempted.");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
