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

    public Task<object?> ListAsync(CancellationToken ct) =>
        SyncAsync(null, 0, 100, ct);

    /// <summary>
    /// Stateless, server-assigned per-session pagination. Do not use device
    /// clocks as cursors or mistake a message timestamp for a delivery receipt.
    /// Existing clients without query parameters read the Main conversation.
    /// </summary>
    public async Task<object?> SyncAsync(Guid? sessionId, long after, int limit,
        CancellationToken ct)
    {
        if (after < 0 || limit is < 1 or > 100 ||
            sessionId == Guid.Empty)
            throw new ArgumentException("Use a valid sessionId, after >= 0 and limit 1–100.");

        var id = sessionId ?? GatewayConversationSchema.DefaultSessionId;
        var now = DateTimeOffset.UtcNow;
        return await store.ReadAsync(state =>
        {
            var session = state.ConversationSessions.FirstOrDefault(x => x.Id == id);
            if (session is null) return null;

            var available = state.ConversationMessages
                .Where(x => x.SessionId == id &&
                    x.CreatedAtUtc >= now.AddDays(-RetentionDays) &&
                    x.CreatedAtUtc <= now)
                .OrderBy(x => x.Sequence)
                .ToArray();
            // A cursor can be far behind retention. Explicitly disclose
            // missing history rather than displaying a seemingly complete
            // transcript after an offline device reconnects.
            var oldestAvailableSequence = available.Length > 0
                ? available[0].Sequence
                : session.LastSequence > 0 ? session.LastSequence + 1 : 0;
            var lostPrefix = oldestAvailableSequence > 0 &&
                after < oldestAvailableSequence - 1;
            var missingBeforePage = lostPrefix
                ? oldestAvailableSequence - 1 - after
                : 0;
            var candidates = available.Where(x => x.Sequence > after)
                .Take(limit + 1).ToArray();
            var page = candidates.Take(limit).ToArray();
            return (object)new
            {
                sessionId = id,
                messages = page.Select(x => new
                {
                    id = x.Id,
                    clientMessageId = x.ClientMessageId,
                    deviceId = x.DeviceId,
                    role = x.Role,
                    replyToMessageId = x.ReplyToMessageId,
                    content = x.Content,
                    createdAtUtc = x.CreatedAtUtc,
                    sessionId = x.SessionId,
                    sequence = x.Sequence
                }).ToArray(),
                nextAfter = page.Length == 0 ? after : page[^1].Sequence,
                hasMore = candidates.Length > limit,
                highWatermark = session.LastSequence,
                oldestAvailableSequence,
                historyGapDetected = lostPrefix,
                missingMessageCountBeforePage = missingBeforePage,
                archived = session.ArchivedAtUtc is not null,
                retentionDays = RetentionDays,
                savedOnly = true,
                assistantConnected = false,
                note = lostPrefix
                    ? "Some earlier messages are no longer available under local retention. Do not treat this page as a complete transcript."
                    : "Encrypted local conversation sync only. No live mobile AI or external service is connected."
            };
        }, ct);
    }

    public async Task<object> ListSessionsAsync(CancellationToken ct)
    {
        return await store.ReadAsync(state => (object)new
        {
            sessions = state.ConversationSessions.OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    id = x.Id,
                    title = x.Title,
                    archivedAtUtc = x.ArchivedAtUtc,
                    lastSequence = x.LastSequence
                }).ToArray(),
            defaultSessionId = GatewayConversationSchema.DefaultSessionId,
            localOnly = true
        }, ct);
    }

    public async Task<(string Outcome, Guid? Id)> CreateSessionAsync(
        GatewayDevice device, CreateGatewaySessionRequest request,
        CancellationToken ct)
    {
        var title = request.Title?.Trim() ?? "";
        if (title.Length is < 2 or > 60 || title.Any(char.IsControl))
            throw new ArgumentException("Session title must contain 2–60 visible characters.");

        return await store.UpdateAsync(state =>
        {
            if (!WritableDevice(state, device, DateTimeOffset.UtcNow))
                return (Outcome: "unauthorized", Id: (Guid?)null);
            if (state.ConversationSessions.Count >= 10)
                return (Outcome: "limit", Id: (Guid?)null);
            var session = new GatewayConversationSession { Title = title };
            state.ConversationSessions.Add(session);
            GatewayAudit.Add(state, "conversation.session_created", device.Id);
            return (Outcome: "saved", Id: (Guid?)session.Id);
        }, ct);
    }

    public async Task<string> ArchiveSessionAsync(
        GatewayDevice device, Guid sessionId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty ||
            sessionId == GatewayConversationSchema.DefaultSessionId)
            return "invalid";

        return await store.UpdateAsync(state =>
        {
            if (!WritableDevice(state, device, DateTimeOffset.UtcNow))
                return "unauthorized";
            var session = state.ConversationSessions.FirstOrDefault(x => x.Id == sessionId);
            if (session is null) return "not_found";
            if (session.ArchivedAtUtc is not null) return "archived";
            session.ArchivedAtUtc = DateTimeOffset.UtcNow;
            GatewayAudit.Add(state, "conversation.session_archived", device.Id);
            return "archived";
        }, ct);
    }

    private static bool WritableDevice(GatewayState state, GatewayDevice device,
        DateTimeOffset now) =>
        state.Devices.Any(x => x.Id == device.Id &&
            x.RevokedAtUtc is null && x.ExpiresAtUtc > now &&
            x.TokenHashHex == device.TokenHashHex &&
            x.Scopes.Contains("conversation:write", StringComparer.Ordinal));

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
                    sessionId = x.SessionId,
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
            if (!WritableDevice(state, device, now))
                return new AppendResult("unauthorized", null, null,
                    "Device credential is no longer valid.");

            var sessionId = request.SessionId ?? GatewayConversationSchema.DefaultSessionId;
            var session = state.ConversationSessions.FirstOrDefault(x => x.Id == sessionId);
            if (session is null) return new AppendResult("not_found", null, null,
                "Conversation session does not exist.");
            if (session.ArchivedAtUtc is not null)
                return new AppendResult("archived", null, null,
                    "Conversation session is archived and cannot accept new messages.");

            // Restrict storage to one week and cap total retained records.
            state.ConversationMessages.RemoveAll(x =>
                x.CreatedAtUtc < now.AddDays(-RetentionDays));
            var found = state.ConversationMessages.FirstOrDefault(x =>
                x.DeviceId == device.Id && x.ClientMessageId == request.ClientMessageId);

            if (found is not null)
            {
                if (found.SessionId != sessionId ||
                    !string.Equals(found.Content, text, StringComparison.Ordinal))
                    return new AppendResult("conflict", found.Id, found.CreatedAtUtc,
                        "Client message ID was already used with different content. Nothing changed.");
                return new AppendResult("duplicate", found.Id, found.CreatedAtUtc,
                    "This message was already saved. No duplicate created or AI request sent.");
            }

            var message = new GatewayConversationMessage
            {
                DeviceId = device.Id,
                ClientMessageId = request.ClientMessageId,
                SessionId = sessionId,
                Sequence = checked(++session.LastSequence),
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

            var session = state.ConversationSessions.FirstOrDefault(x => x.Id == original.SessionId);
            if (session is null || session.ArchivedAtUtc is not null)
                return new AppendResult("not_found", null, null,
                    "The source session is unavailable or archived.");

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
                SessionId = original.SessionId,
                Sequence = checked(++session.LastSequence),
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
