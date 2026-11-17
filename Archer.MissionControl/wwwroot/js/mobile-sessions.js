'use strict';

/*
 * Read-only inspection of LOCAL, encrypted gateway sessions. Auth is handled
 * by the owner-enabled desktop bridge; no AI/SMS/push/phone call or server
 * mutation is triggered by this page. Every text value is rendered as text.
 */
(() => {
  const el = id => document.getElementById(id);
  const select = el('mobileSessionSelect');
  const refreshButton = el('mobileSessionRefresh');
  const messages = el('mobileSessionMessages');
  const status = el('mobileSessionStatus');
  const retentionWarning = el('mobileSessionRetentionWarning');
  const more = el('mobileSessionMore');
  let selectedId = '';
  let after = 0;
  let busy = false;
  let activeRequest = 0;

  function textNode(tag, value, className = '') {
    const node = document.createElement(tag);
    node.textContent = String(value ?? '');
    if (className) node.className = className;
    return node;
  }

  function clear(message) {
    messages.replaceChildren(textNode('p', message, 'developer-empty'));
    more.hidden = true;
    more.disabled = true;
  }

  function clearRetentionWarning() {
    retentionWarning.textContent = '';
    retentionWarning.hidden = true;
  }

  // Keep this warning visible across later pages. A page fetched after the
  // gap will no longer report it, but that cannot restore missing history.
  function showRetentionWarning(result) {
    if (result.historyGapDetected !== true) return;
    const count = result.missingMessageCountBeforePage;
    if (!Number.isSafeInteger(count) || count < 1)
      throw new Error('Invalid conversation retention warning.');
    retentionWarning.textContent = count + ' earlier message' +
      (count === 1 ? ' is' : 's are') +
      ' no longer available under local retention (' + result.retentionDays +
      ' days). This transcript is incomplete; the missing messages cannot be recovered here.';
    retentionWarning.hidden = false;
  }

  function setBusy(value) {
    busy = value;
    select.disabled = value || !selectedId;
    refreshButton.disabled = value;
    more.disabled = value || more.hidden;
  }

  function displayMessage(item) {
    const card = document.createElement('article');
    card.className = 'mobile-session-message ' +
      (item.role === 'assistant' ? 'mobile-session-assistant' : 'mobile-session-owner');
    const meta = textNode('div', '', 'mobile-session-meta');
    meta.appendChild(textNode('strong', item.role === 'assistant' ? 'Archer' : 'Owner'));
    const date = new Date(item.createdAtUtc);
    const dateLabel = Number.isFinite(date.getTime()) ? date.toLocaleString() : 'Time unavailable';
    meta.appendChild(textNode('small', '#' + item.sequence + ' · ' + dateLabel));
    card.appendChild(meta);
    card.appendChild(textNode('p', item.content, 'mobile-session-message-text'));
    messages.appendChild(card);
  }

  async function loadPage(reset = false) {
    if (!selectedId || busy || appState.busy) return;
    const requestId = ++activeRequest;
    setBusy(true);
    const cursor = reset ? 0 : after;
    if (reset) {
      after = 0;
      clearRetentionWarning();
      clear('Loading this local conversation…');
    }
    try {
      const path = '/api/mobile/session-history?sessionId=' +
        encodeURIComponent(selectedId) + '&after=' + cursor + '&limit=20';
      const result = await api(path);
      if (requestId !== activeRequest) return;
      if (!result.connected || result.sessionId !== selectedId)
        throw new Error(result.note || 'Local session is not available.');
      if (reset) messages.replaceChildren();
      if (!Array.isArray(result.messages) ||
          typeof result.historyGapDetected !== 'boolean')
        throw new Error('Invalid history response.');
      showRetentionWarning(result);
      if (result.messages.length) {
        const empty = messages.querySelector('.developer-empty');
        if (empty) empty.remove();
        for (const item of result.messages) displayMessage(item);
      } else if (!messages.children.length) {
        clear('No retained messages in this local conversation.');
      }
      after = result.nextAfter;
      more.hidden = !result.hasMore;
      more.disabled = !result.hasMore;
      const context = result.archived ? 'Archived session' : 'Local session';
      status.textContent = context + ' · ' +
        (retentionWarning.hidden ? 'Retained history view' : 'Incomplete history') +
        ' · ' + result.highWatermark + ' total sequence positions · ' +
        (result.hasMore ? 'More messages available' : 'Showing available retained messages');
    } catch (error) {
      if (requestId === activeRequest) {
        status.textContent = 'Cannot load local conversation: ' + error.message;
        more.hidden = true;
      }
    } finally {
      if (requestId === activeRequest) setBusy(false);
    }
  }

  async function refresh() {
    if (busy || appState.busy) return;
    const requestId = ++activeRequest;
    setBusy(true);
    try {
      const data = await api('/api/mobile/sessions');
      if (!data.connected) {
        selectedId = '';
        select.replaceChildren(textNode('option', 'Local gateway is disabled or unavailable'));
        status.textContent = data.note || 'Gateway not connected.';
        clearRetentionWarning();
        clear('No mobile conversation data loaded.');
        return;
      }
      const sessions = data.sessions || [];
      const chosen = sessions.some(x => x.id === selectedId) ? selectedId :
        data.defaultSessionId;
      select.replaceChildren();
      for (const session of sessions) {
        const option = document.createElement('option');
        option.value = session.id;
        option.textContent = session.title + (session.archivedAtUtc ? ' (archived)' : '');
        select.appendChild(option);
      }
      selectedId = chosen;
      select.value = chosen;
      status.textContent = 'Choose a session to view its encrypted local journal.';
    } catch (error) {
      selectedId = '';
      select.replaceChildren(textNode('option', 'Session catalog unavailable'));
      status.textContent = 'Cannot list local mobile sessions. No message or AI action was sent.';
      clearRetentionWarning();
      clear('No mobile conversation data loaded.');
      return;
    } finally {
      if (requestId === activeRequest) setBusy(false);
    }
    if (selectedId) await loadPage(true);
  }

  select.addEventListener('change', () => {
    selectedId = select.value;
    after = 0;
    loadPage(true);
  });
  refreshButton.addEventListener('click', refresh);
  more.addEventListener('click', () => loadPage(false));

  window.ArcherMobileSessions = { refresh, refreshMessages: () => loadPage(true) };
  refresh();
})();
