'use strict';

/*
 * Archer's first cross-channel-ready LOCAL inbox.
 * Every item is backed by a mission failure or site incident.
 * This code never invokes Twilio, APNs or the AI API.
 */
(() => {
  const el = id => document.getElementById(id);
  const container = el('inboxItems');
  const filter = el('inboxFilter');
  const refreshButton = el('inboxRefreshButton');
  let latest = [];
  let fetching = false;
  let modifying = false;

  const textNode = (tag, value, className) => {
    const node = document.createElement(tag);
    node.textContent = String(value ?? '');
    if (className) node.className = className;
    return node;
  };

  function updateBadge(data) {
    const pending = Number(data.needsReviewCount || 0);
    const unseen = Number(data.unreadCount || 0);
    el('inboxTotals').textContent =
      unseen + ' unread · ' + pending + ' needing review';
    const badge = el('inboxNavCount');
    badge.textContent = pending;
    badge.classList.toggle('hidden', pending === 0);
  }

  function render() {
    const mode = filter.value;
    const items = latest.filter(item => {
      if (mode === 'unread') return !item.readAtUtc;
      if (mode === 'action')
        return item.priority !== 'Information' && !item.acknowledgedAtUtc && !item.resolvedAtUtc;
      return true;
    });
    container.replaceChildren();

    if (!items.length) {
      const box = textNode('p', mode === 'all'
        ? 'No events recorded. This only means the local connected sources have no inbox items.'
        : 'No inbox items match this filter.', 'developer-empty');
      container.appendChild(box);
      return;
    }

    for (const item of items) {
      const card = document.createElement('article');
      card.className = 'inbox-card';
      if (!item.readAtUtc) card.classList.add('inbox-unread');
      if (item.priority === 'UrgentReview') card.classList.add('inbox-urgent');

      const main = document.createElement('div');
      main.className = 'inbox-details';
      const meta = document.createElement('div');
      meta.className = 'inbox-meta';
      const priority = item.priority === 'UrgentReview' ? 'Urgent review' :
        item.priority === 'Information' ? 'Information' : 'Attention';
      meta.appendChild(textNode('span', priority, 'inbox-priority'));
      meta.appendChild(textNode('span', item.category, 'inbox-category'));
      const date = new Date(item.occurredAtUtc);
      meta.appendChild(textNode('time', Number.isFinite(date.getTime()) ? date.toLocaleString() : 'Unknown date'));
      main.appendChild(meta);

      main.appendChild(textNode('h3', item.title));
      main.appendChild(textNode('p', item.summary));
      const flags = [];
      if (item.resolvedAtUtc) flags.push('Source condition resolved');
      if (item.acknowledgedAtUtc) flags.push('Acknowledged');
      else if (!item.readAtUtc) flags.push('Unread');
      if (flags.length) main.appendChild(textNode('small', flags.join(' · '), 'inbox-flags'));
      card.appendChild(main);

      const actions = document.createElement('div');
      actions.className = 'inbox-actions';
      if (!item.readAtUtc) {
        const read = textNode('button', 'Mark read', 'button secondary');
        read.type = 'button';
        read.dataset.inboxId = item.id;
        read.dataset.inboxAction = 'read';
        actions.appendChild(read);
      }
      if (!item.acknowledgedAtUtc && item.priority !== 'Information') {
        const acknowledge = textNode('button', 'Acknowledge', 'button secondary');
        acknowledge.type = 'button';
        acknowledge.dataset.inboxId = item.id;
        acknowledge.dataset.inboxAction = 'acknowledge';
        actions.appendChild(acknowledge);
      }
      if (item.missionId) {
        const go = textNode('button', 'Open mission', 'button secondary');
        go.type = 'button';
        go.dataset.inboxMission = item.missionId;
        actions.appendChild(go);
      } else if (item.websiteMonitorId) {
        const go = textNode('button', 'Open Operations', 'button secondary');
        go.type = 'button';
        go.dataset.inboxOperations = 'true';
        actions.appendChild(go);
      }
      card.appendChild(actions);
      container.appendChild(card);
    }
  }

  async function refresh() {
    if (fetching || modifying) return;
    fetching = true;
    try {
      const result = await api('/api/inbox');
      latest = result.items || [];
      updateBadge(result);
      if (!el('inboxView').classList.contains('hidden')) render();
    } catch (error) {
      el('inboxTotals').textContent = 'Inbox unavailable: ' + error.message;
    } finally {
      fetching = false;
    }
  }

  container.addEventListener('click', async event => {
    const mission = event.target.closest('button[data-inbox-mission]');
    if (mission) {
      appState.selectedMissionId = mission.dataset.inboxMission;
      showView('missions');
      render();
      // app.js has a same-named render() for missions in global scope.
      window.refresh?.().catch(error => console.warn('Mission refresh failed:', error.message));
      return;
    }
    if (event.target.closest('button[data-inbox-operations]')) {
      showView('operations');
      return;
    }

    const button = event.target.closest('button[data-inbox-id]');
    if (!button || modifying || appState.busy) return;
    modifying = true;
    button.disabled = true;
    const id = button.dataset.inboxId;
    const action = button.dataset.inboxAction;
    try {
      await api('/api/inbox/' + encodeURIComponent(id) + '/' + action, {
        method: 'POST', body: '{}'
      });
      // Release the local lock before refresh so read state is reflected immediately.
    } catch (error) {
      notice('Inbox update failed: ' + error.message, true);
    } finally {
      modifying = false;
      fetching = false;
      await refresh();
    }
  });

  refreshButton.addEventListener('click', refresh);
  filter.addEventListener('change', render);

  window.ArcherInbox = { refresh };
  refresh();
  window.setInterval(() => {
    if (document.hidden || appState.busy) return;
    refresh();
  }, 30000);
})();