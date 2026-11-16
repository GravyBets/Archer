'use strict';

/*
 * LOCAL desktop preview of phone-authored drafts.
 * No automatic AI model call, chat submission, SMS, device tracking,
 * or movement of existing desktop conversation history.
 */
(() => {
  const el = id => document.getElementById(id);
  const connection = el('mobileDraftConnection');
  const note = el('mobileDraftNote');
  const items = el('mobileDraftItems');
  const refreshButton = el('mobileDraftRefresh');
  const shareButton = el('mobileDraftShareReply');
  const composer = el('chatText');
  let busy = false;
  let selected = null;
  let pendingShare = null;

  const updateShare = () => {
    shareButton.disabled = !pendingShare || busy || appState.busy;
  };

  const element = (tag, content, className = '') => {
    const node = document.createElement(tag);
    node.textContent = String(content ?? '');
    if (className) node.className = className;
    return node;
  };

  function render(result) {
    items.replaceChildren();
    const enabled = !!result.enabled;
    const connected = !!result.connected;
    connection.textContent = !enabled ? 'DISABLED' : connected ? 'LOCAL / READ ONLY' : 'UNAVAILABLE';
    note.textContent = result.note ||
      'No mobile gateway message preview is available. Nothing was sent.';

    if (!enabled || !connected || !(result.messages || []).length) {
      items.appendChild(element('p',
        enabled && connected
          ? 'No phone-authored drafts stored in the local gateway.'
          : 'To enable this local-only bridge, configure a separate matching secret on your home PC.',
        'developer-empty'));
      return;
    }

    const messages = result.messages;
    for (const message of messages) {
      const card = document.createElement('article');
      card.className = 'mobile-draft-item';

      const header = element('div', '', 'mobile-draft-meta');
      const date = new Date(message.createdAtUtc);
      header.appendChild(element('strong', 'Phone-authored draft'));
      header.appendChild(element('small',
        Number.isFinite(date.getTime()) ? date.toLocaleString() : 'Time unavailable'));
      card.appendChild(header);
      card.appendChild(element('p', message.content, 'mobile-draft-text'));

      const button = element('button', 'Use in chat composer', 'button secondary');
      button.type = 'button';
      button.addEventListener('click', () => {
        if (appState.busy) {
          notice('Finish the current request before preparing another chat.', true);
          return;
        }
        // Explicitly fill text only; NEVER dispatch submit or call /api/chat.
        selected = { id: message.id, content: message.content };
        pendingShare = null;
        updateShare();
        composer.value = message.content;
        composer.dispatchEvent(new Event('input', { bubbles: true }));
        composer.focus();
        note.textContent = 'Draft copied. Keep its text unchanged to make the reply shareable; press Send yourself first.';
      });
      card.appendChild(button);
      items.appendChild(card);
    }
  }

  async function refresh() {
    if (busy || appState.busy) return;
    busy = true;
    refreshButton.disabled = true;
    try {
      const result = await api('/api/mobile/conversation-preview');
      render(result);
    } catch (error) {
      connection.textContent = 'UNAVAILABLE';
      note.textContent = 'Phone draft preview failed. No AI request or external action was made.';
      items.replaceChildren();
    } finally {
      busy = false;
      refreshButton.disabled = false;
    }
  }

  function beginChat(text) {
    // Every new chat invalidates a pending share for the previous response.
    pendingShare = null;
    updateShare();
    return selected && selected.content === text
      ? { id: selected.id, question: selected.content }
      : null;
  }

  function chatCompleted(source) {
    if (!source || !selected || selected.id !== source.id) return;
    pendingShare = source;
    updateShare();
    note.textContent = 'Archer replied on the desktop. Choose “Share last reply with mobile” to copy it to the encrypted local gateway. No reply is shared automatically.';
  }

  shareButton.addEventListener('click', async () => {
    if (!pendingShare || busy || appState.busy) return;
    const source = pendingShare;
    if (!window.confirm('Share Archer’s most recent reply with your enrolled local mobile devices? This does not send any notification or make an AI request.')) return;
    busy = true;
    updateShare();
    try {
      const result = await api('/api/mobile/reply-publish', {
        method: 'POST',
        body: JSON.stringify({
          gatewayMessageId: source.id,
          question: source.question
        })
      });
      if (!result.shared) throw new Error(result.message || 'Reply was not shared.');
      pendingShare = null;
      selected = null;
      note.textContent = result.message;
      window.ArcherMobileSessions?.refreshMessages();
      // Refresh after releasing the local action lock below.
    } catch (error) {
      // Keep the source so a timed-out local write can be safely retried:
      // gateway idempotency forbids a second differing assistant reply.
      note.textContent = 'Could not confirm sharing: ' + error.message +
        ' Retrying this button does not make another AI request.';
    } finally {
      busy = false;
      updateShare();
      refresh();
    }
  });

  refreshButton.addEventListener('click', refresh);
  window.ArcherMobileDrafts = { refresh, beginChat, chatCompleted };
  refresh();

  window.setInterval(() => {
    if (document.hidden || appState.busy || el('archerView').classList.contains('hidden')) return;
    refresh();
  }, 45_000);
})();