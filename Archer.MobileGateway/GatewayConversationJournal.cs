namespace Archer.MobileGateway;

/// <summary>
/// Local, owner-scoped conversation JOURNAL for the future iPhone client.
/// This does not call an AI model, mirror desktop chat, deliver push, or
/// promise a reply. All text is user-supplied and stored only in this
/// separate gateway's private, Git-ignored JSON state file.
/// </summary>
public sealed class GatewayConversationJournal(GatewayStore store)
{
    private const int RetentionDays = 7;
    private const int HistoryLimit = 200;
    private const int MaxMessageLength = 1600;

    public sealed record AppendResult(string Outcome, Guid? Id, DateTimeOffset? AtUtc,
        string Note);

    public async Task<object> ListAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await store.ReadAsync(state => (object)new
        {
            messages = state.ConversationMessages
                .Where(x => x.CreatedAtUtc >= now.AddDays(-RetentionDays) &&
                            x.CreatedAtUtc <= now)
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .TakeLast(100)
                .Select(x => new
                {
                    id = x.Id,
                    clientMessageId = x.ClientMessageId,
                    deviceId = x.DeviceId,
                    role = x.Role,
                    content = x.Content,
                    createdAtUtc = x.CreatedAtUtc
                }).ToArray(),
            retentionDays = RetentionDays,
            savedOnly = true,
            assistantConnected = false,
            note = "Shared local journal only. This is not yet connected to desktop chat or an AI responder."
        }, ct);
    }

    public async Task<AppendResult> AppendAsync(
        GatewayDevice device, AppendConversationMessageRequest request, CancellationToken ct)
    {
        var text = request.Content?.Trim() ?? "";
        if (request.ClientMessageId == Guid.Empty ||
            text.Length is < 1 or > MaxMessageLength ||
            text.Any(c => char.IsControl(c) && c is not '\n' and not '\t'))
            throw new ArgumentException("Use a nonzero clientMessageId and 1–1600 characters of text without control characters.");

        var now = DateTimeOffset.UtcNow;
        return await store.UpdateAsync(state =>
        {
            // Restrict storage to one week and cap total retained records.
            state.ConversationMessages.RemoveAll(x =>
                x.CreatedAtUtc < now.AddDays(-RetentionDays));
            var found = state.ConversationMessages.FirstOrDefault(x =>
                x.DeviceId == device.Id && x.ClientMessageId == request.ClientMessageId);

            if (found is not null)
            {
                if (!string.Equals(found.Content, text, StringComparison.Ordinal))
                    return new AppendResult("conflict", found.Id, found.CreatedAtUtc,
                        "Client message ID was already used with different content. Nothing changed.");
                return new AppendResult("duplicate", found.Id, found.CreatedAtUtc,
                    "This message was already saved. No duplicate created or AI request sent.");
            }

            var message = new GatewayConversationMessage
            {
                DeviceId = device.Id,
                ClientMessageId = request.ClientMessageId,
                Content = text,
                CreatedAtUtc = now
            };
            state.ConversationMessages.Add(message);
            if (state.ConversationMessages.Count > HistoryLimit)
                state.ConversationMessages = state.ConversationMessages
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .ThenByDescending(x => x.Id)
                    .Take(HistoryLimit).ToList();

            GatewayAudit.Add(state, "conversation.message_saved", device.Id);
            return new AppendResult("saved", message.Id, now,
                "Text saved locally for future conversation integration. No AI reply or notification was generated.");
        }, ct);
    }

    public async Task<int> ClearAsync(CancellationToken ct)
    {
        return await store.UpdateAsync(state =>
        {
            var count = state.ConversationMessages.Count;
            state.ConversationMessages.Clear();
            GatewayAudit.Add(state, "conversation.cleared");
            return count;
        }, ct);
    }
}
