namespace Archer.MobileGateway;

/// <summary>
/// No-charge notification policy sandbox. Never contacts APNs, Twilio, OpenAI
/// or a handset. The output is a durable advisory preview, not a dispatch.
/// </summary>
public sealed class GatewayDeliverySimulator(GatewayStore store)
{
    public async Task<object> SimulateAsync(SimulateDeliveryRequest request,
        CancellationToken ct)
    {
        if (!request.Confirm || request.MaxEvents is < 1 or > 20)
            throw new ArgumentException("Confirm a dry run and choose 1–20 events.");

        return await store.UpdateAsync(state =>
        {
            var now = DateTimeOffset.UtcNow;
            var devices = state.Devices
                .Where(d => d.RevokedAtUtc is null && d.ExpiresAtUtc > now &&
                    d.Scopes.Contains("inbox:read", StringComparer.Ordinal))
                .Take(5).ToArray();
            var events = state.Events.OrderByDescending(e => e.OccurredAtUtc)
                .Take(request.MaxEvents).ToArray();
            var created = 0;
            var duplicates = 0;

            foreach (var item in events)
            foreach (var device in devices)
            {
                var key = item.Id.ToString("N") + ":" + device.Id.ToString("N") +
                    ":sandbox-policy-1";
                if (state.DeliveryPreviews.Any(p => p.IdempotencyKey == key))
                {
                    duplicates++;
                    continue;
                }

                var resolved = item.ResolvedAtUtc is not null;
                var acknowledged = item.AcknowledgedAtUtc is not null;
                var stale = item.OccurredAtUtc < now.AddMinutes(-20);
                var urgentNeedsReview = item.Priority == "UrgentReview";

                // A three-strike reachability check is NOT a verified business
                // outage. It never directly creates an interrupting phone route.
                var channel = resolved || acknowledged ? "none" :
                    urgentNeedsReview && !stale ? "push-preview" :
                    item.Priority == "Attention" && !stale ? "push-preview" :
                    "digest-preview";
                var stateValue = resolved ? "SuppressedResolved" :
                    acknowledged ? "SuppressedAcknowledged" : "Simulated";
                var reason = resolved ? "The source condition was resolved; no new alert." :
                    acknowledged ? "Owner already acknowledged this event." :
                    stale ? "Stale source; preview a future digest instead of interrupting." :
                    urgentNeedsReview
                        ? "Reachability needs investigation. An urgent call is NOT authorized by a check alone."
                        : item.Priority == "Attention"
                            ? "Routine attention is eligible for a future push after opt-in."
                            : "Information goes to a future digest; no immediate interruption.";

                state.DeliveryPreviews.Insert(0, new GatewayDeliveryPreview
                {
                    EventId = item.Id,
                    DeviceId = device.Id,
                    IdempotencyKey = key,
                    Channel = channel,
                    State = stateValue,
                    Reason = reason,
                    InterruptOwner = false,
                    RequiresIncidentVerification = urgentNeedsReview
                });
                created++;
            }

            state.DeliveryPreviews = state.DeliveryPreviews
                .OrderByDescending(p => p.SimulatedAtUtc)
                .ThenByDescending(p => p.Id)
                .Take(200).ToList();
            return (object)new
            {
                activeDeviceCount = devices.Length,
                selectedEventCount = events.Length,
                newPreviews = created,
                duplicatePreviewsSuppressed = duplicates,
                actualNotificationsSent = 0,
                providerCharges = false,
                message = "Simulation only. No APNs, SMS, call, AI request or device notification occurred."
            };
        }, ct);
    }

    public async Task<object> GetForDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        return await store.ReadAsync(state => (object)new
        {
            previews = state.DeliveryPreviews
                .Where(p => p.DeviceId == deviceId)
                .OrderByDescending(p => p.SimulatedAtUtc)
                .Take(50)
                .Select(p => new
                {
                    id = p.Id,
                    eventId = p.EventId,
                    channel = p.Channel,
                    state = p.State,
                    reason = p.Reason,
                    interruptOwner = p.InterruptOwner,
                    requiresIncidentVerification = p.RequiresIncidentVerification,
                    simulatedAtUtc = p.SimulatedAtUtc
                }).ToArray(),
            actualNotificationsSent = 0,
            note = "Sandbox previews are not push notifications or proof of delivery."
        }, ct);
    }

    public async Task<object> GetInternalSummaryAsync(CancellationToken ct)
    {
        return await store.ReadAsync(state => (object)new
        {
            totalPreviews = state.DeliveryPreviews.Count,
            previews = state.DeliveryPreviews.OrderByDescending(p => p.SimulatedAtUtc)
                .Take(50).ToArray(),
            actualNotificationsSent = 0
        }, ct);
    }
}
