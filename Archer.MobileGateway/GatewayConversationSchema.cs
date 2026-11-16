namespace Archer.MobileGateway;

/// <summary>
/// Single owner, local-only conversation session layout. No multi-user
/// identity, AI calls or remote listening. Upgrades existing v1.8 messages
/// in the encrypted state in place, retaining old IDs and reply links.
/// </summary>
public static class GatewayConversationSchema
{
    // Stable on every run and across migrations; never derived from device IDs.
    public static readonly Guid DefaultSessionId =
        Guid.Parse("8ceea3c0-13ec-4ef7-84d2-3a51b05d98ff");

    public static bool Upgrade(GatewayState state)
    {
        var changed = false;
        state.ConversationSessions ??= [];
        state.ConversationMessages ??= [];

        if (!state.ConversationSessions.Any(x => x.Id == DefaultSessionId))
        {
            state.ConversationSessions.Insert(0, new GatewayConversationSession
            {
                Id = DefaultSessionId,
                Title = "Main conversation",
                CreatedAtUtc = DateTimeOffset.UnixEpoch
            });
            changed = true;
        }

        foreach (var message in state.ConversationMessages)
        {
            if (message.SessionId != Guid.Empty) continue;
            // Legacy, pre-session messages were all in one local journal.
            message.SessionId = DefaultSessionId;
            changed = true;
        }

        foreach (var session in state.ConversationSessions)
        {
            // Deterministically assign pre-v1.9 messages a cursor in the same
            // order clients previously saw. Never reuse an existing cursor.
            var allocated = state.ConversationMessages
                .Where(x => x.SessionId == session.Id && x.Sequence > 0)
                .Select(x => x.Sequence).DefaultIfEmpty(0).Max();
            var next = Math.Max(allocated, session.LastSequence);
            foreach (var legacy in state.ConversationMessages
                .Where(x => x.SessionId == session.Id && x.Sequence <= 0)
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id))
            {
                legacy.Sequence = checked(++next);
                changed = true;
            }

            if (session.LastSequence < next)
            {
                session.LastSequence = next;
                changed = true;
            }
        }
        return changed;
    }
}
