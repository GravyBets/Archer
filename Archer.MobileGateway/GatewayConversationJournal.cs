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
                    replyToMessageId = x.ReplyToMessageId,
                    content = x.Content,
                    createdAtUtc = x.CreatedAtUtc
                }).ToArray(),
            retentionDays = RetentionDays,
            savedOnly = true,
            assistantConnected = false,
            note = "Shared local journal only. This is not yet connected to desktop chat or an AI responder."
        }, ct);
    }

    /// <summary>
    /// An additional, separately keyed LOCAL service-to-service preview.
    /// Does not mark messages handled, import desktop history, charge AI,
    /// send assistant responses, or grant an action to a mobile client.
    /// </summary>
    public async Task<object> BridgePreviewAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await store.ReadAsync(state => (object)new
        {
            messages = state.ConversationMessages
                .Where(x => x.Role == "owner" &&
                    x.CreatedAtUtc >= now.AddDays(-RetentionDays) &&
                    x.CreatedAtUtc <= now &&
                    !state.ConversationMessages.Any(reply =>
                        reply.Role == "assistant" && reply.ReplyToMessageId == x.Id))
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .TakeLast(30)
                .Select(x => new
                {
                    id = x.Id,
                    deviceId = x.DeviceId,
                    content = x.Content,
                    createdAtUtc = x.CreatedAtUtc
                }).ToArray(),
            maxMessages = 30,
            desktopHistorySynced = false,
            aiReplyGenerated = false,
            actionExecuted = false,
            note = "Read-only local phone-draft preview; nothing has been sent to Archer's AI."
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
            // Repeat the access check inside the same atomic state update:
            // a token rotated/revoked between HTTP auth and write cannot post.
            var current = state.Devices.FirstOrDefault(x =>
                x.Id == device.Id &&
                x.RevokedAtUtc is null &&
                x.ExpiresAtUtc > now &&
                x.TokenHashHex == device.TokenHashHex &&
                x.Scopes.Contains("conversation:write", StringComparer.Ordinal));
            if (current is null)
                return new AppendResult("unauthorized", null, null,
                    "Device credential is no longer valid.");

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

    /// <summary>
    /// A reply may be created ONLY through the distinct local bridge-key route,
    /// not a mobile Bearer. Require an exact existing owner draft, an exact
    /// original question, and at most one assistant reply per draft.
    /// No AI or phone provider is contacted; this only persists text.
    /// </summary>
    public async Task<AppendResult> PublishAssistantReplyAsync(
        PublishGatewayReplyRequest request, CancellationToken ct)
    {
        var question = request.ExpectedOwnerText?.Trim() ?? "";
        var answer = request.Content?.Trim() ?? "";
        if (request.InReplyToId == Guid.Empty ||
            question.Length is < 1 or > MaxMessageLength ||
            answer.Length is < 1 or > 4000 ||
            answer.Any(c => char.IsControl(c) && c is not '\n' and not '\t') ||
            question.Any(c => char.IsControl(c) && c is not '\n' and not '\t'))
            throw new ArgumentException("A valid draft ID, exact owner question and 1–4000 character reply are required.");

        var now = DateTimeOffset.UtcNow;
        return await store.UpdateAsync(state =>
        {
            var original = state.ConversationMessages.FirstOrDefault(x =>
                x.Id == request.InReplyToId && x.Role == "owner" &&
                x.CreatedAtUtc >= now.AddDays(-RetentionDays) &&
                x.CreatedAtUtc <= now &&
                string.Equals(x.Content, question, StringComparison.Ordinal));
            if (original is null)
                return new AppendResult("not_found", null, null,
                    "No exact matching current owner draft was found. No assistant text saved.");

            var previous = state.ConversationMessages.FirstOrDefault(x =>
                x.Role == "assistant" && x.ReplyToMessageId == original.Id);
            if (previous is not null)
            {
                return string.Equals(previous.Content, answer, StringComparison.Ordinal)
                    ? new AppendResult("duplicate", previous.Id, previous.CreatedAtUtc,
                        "Reply was already shared; no duplicate message created.")
                    : new AppendResult("conflict", previous.Id, previous.CreatedAtUtc,
                        "This draft already has a different Archer reply. No overwrite allowed.");
            }

            var message = new GatewayConversationMessage
            {
                DeviceId = Guid.Empty, // trusted local desktop service, not a device
                ClientMessageId = original.Id,
                ReplyToMessageId = original.Id,
                Role = "assistant",
                Content = answer,
                CreatedAtUtc = now
            };
            state.ConversationMessages.Add(message);
            if (state.ConversationMessages.Count > HistoryLimit)
                state.ConversationMessages = state.ConversationMessages
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .ThenByDescending(x => x.Id)
                    .Take(HistoryLimit).ToList();
            GatewayAudit.Add(state, "conversation.assistant_reply_shared",
                original.DeviceId, message.Id);
            return new AppendResult("saved", message.Id, now,
                "Explicitly approved local desktop reply saved to the encrypted mobile journal. No phone notification was delivered.");
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
