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
  const composer = el('chatText');
  let busy = false;

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
        composer.value = message.content;
        composer.dispatchEvent(new Event('input', { bubbles: true }));
        composer.focus();
        note.textContent = 'Draft copied to the composer. Review or edit it, then press Send yourself.';
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

  refreshButton.addEventListener('click', refresh);
  window.ArcherMobileDrafts = { refresh };
  refresh();

  window.setInterval(() => {
    if (document.hidden || appState.busy || el('archerView').classList.contains('hidden')) return;
    refresh();
  }, 45_000);
})();