'use strict';

/* Archer v0.6 — consented owner context and website operations.
 * Presence is one-time, coarse and expiring. No background GPS, no push,
 * no customer/payment connections, and no outbound SMS or phone calls.
 */
(() => {
  const el = id => document.getElementById(id);
  const safe = v => String(v ?? '').replace(/[&<>"']/g,
    c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  let pendingCoordinates = null;
  let presenceLoaded = false;
  let busy = false;

  const activity = el('ownerPresenceActivity');
  const place = el('ownerPresencePlace');
  const duration = el('ownerPresenceDuration');
  const shareAi = el('ownerPresenceShareAi');
  const status = el('ownerPresenceStatus');
  const locationStatus = el('ownerPresenceLocationStatus');

  async function loadPresence() {
    const result = await api('/api/presence');
    if (result.active && result.presence) {
      const p = result.presence;
      const expiry = new Date(p.expiresAtUtc);
      const date = expiry.toLocaleString();
      status.textContent = p.activity +
        (p.placeLabel ? ' · ' + p.placeLabel : '') +
        ' · expires ' + date +
        (p.shareWithAi ? ' · included when you chat with Archer' : ' · private; not sent to the AI');
      if (!presenceLoaded) {
        activity.value = p.activity;
        place.value = p.placeLabel || '';
        shareAi.checked = !!p.shareWithAi;
        const minsLeft = Math.max(1, Math.round((expiry - Date.now()) / 60000));
        duration.value = [15, 60, 240, 480].reduce(
          (best, option) => Math.abs(option - minsLeft) < Math.abs(best - minsLeft) ? option : best, 60);
        if (p.approxLatitude != null && p.approxLongitude != null) {
          pendingCoordinates = { latitude: p.approxLatitude, longitude: p.approxLongitude };
        }
      }
      locationStatus.textContent = p.approxLatitude != null
        ? 'One-time approximate location stored: ' + p.approxLatitude.toFixed(2) +
          ', ' + p.approxLongitude.toFixed(2) + '. No movement tracking.'
        : 'No GPS location saved. Click Share location once if you want to attach an approximate location.';
    } else {
      status.textContent = 'Nothing shared right now. Archer does not know your location or activity.';
      if (!presenceLoaded) pendingCoordinates = null;
      locationStatus.textContent = 'No location shared. One-time browser permission only; coordinates are rounded before saving.';
    }
    presenceLoaded = true;
  }

  el('ownerPresenceSaveButton').addEventListener('click', async () => {
    if (busy || appState.busy) return;
    busy = true;
    try {
      const data = {
        activity: activity.value,
        placeLabel: place.value.trim(),
        durationMinutes: Number(duration.value),
        shareWithAi: shareAi.checked,
        latitude: pendingCoordinates?.latitude ?? null,
        longitude: pendingCoordinates?.longitude ?? null
      };
      await api('/api/presence', { method: 'POST', body: JSON.stringify(data) });
      await loadPresence();
      notice('Presence saved. It will expire automatically; you can clear it at any time.');
    } catch (error) { notice(error.message, true); }
    finally { busy = false; }
  });

  el('ownerPresenceClearButton').addEventListener('click', async () => {
    if (busy || appState.busy) return;
    busy = true;
    try {
      await api('/api/presence', { method: 'DELETE' });
      pendingCoordinates = null;
      activity.value = 'Available';
      place.value = '';
      duration.value = '60';
      shareAi.checked = false;
      await loadPresence();
      notice('Activity and approximate location cleared.');
    } catch (error) { notice(error.message, true); }
    finally { busy = false; }
  });

  el('ownerPresenceLocationButton').addEventListener('click', () => {
    if (busy || appState.busy) return;
    if (!navigator.geolocation) {
      notice('Location is not supported in this browser. You can still set your activity manually.', true);
      return;
    }
    locationStatus.textContent = 'Requesting one-time location permission…';
    navigator.geolocation.getCurrentPosition(position => {
      // Coarse coordinate and no repeated location observations.
      pendingCoordinates = {
        latitude: Number(position.coords.latitude.toFixed(2)),
        longitude: Number(position.coords.longitude.toFixed(2))
      };
      locationStatus.textContent = 'Approximate coordinates ready: ' +
        pendingCoordinates.latitude.toFixed(2) + ', ' +
        pendingCoordinates.longitude.toFixed(2) +
        '. Not saved until you click Save context. No tracking is active.';
    }, error => {
      const names = { 1: 'Location permission denied.', 2: 'Location unavailable.', 3: 'Location request timed out.' };
      locationStatus.textContent = (names[error.code] || 'Location could not be determined.') +
        ' Activity can still be set manually.';
    }, { enableHighAccuracy: false, timeout: 12000, maximumAge: 0 });
  });

  const opsList = el('operationsMonitorList');
  const incidentsList = el('operationsIncidentList');
  let opsBusy = false;

  function renderOperations(data) {
    const monitors = data.monitors || [];
    const incidents = data.incidents || [];
    el('operationsStatus').textContent =
      monitors.filter(m => m.enabled).length + ' monitoring / ' + monitors.length + ' registered';
    opsList.innerHTML = monitors.length ? monitors.map(m => {
      const checked = m.lastCheckedAtUtc ? new Date(m.lastCheckedAtUtc).toLocaleString() : 'Never';
      return '<div class="operations-site">' +
        '<div><strong>' + safe(m.name) + '</strong><small>' + safe(m.url) + '</small>' +
        '<small>Status: ' + safe(m.status) + ' · Last checked: ' + safe(checked) +
        (m.lastHttpStatus ? ' · HTTP ' + m.lastHttpStatus : '') + '</small>' +
        (m.lastError ? '<small class="operations-warning">' + safe(m.lastError) + '</small>' : '') +
        '</div><button type="button" class="button secondary" data-monitor-id="' + safe(m.id) +
        '" data-enable="' + (!m.enabled) + '">' + (m.enabled ? 'Stop monitoring' : 'Start monitoring') +
        '</button></div>';
    }).join('') : '<div class="developer-empty">No websites yet. Add an approved public HTTPS site above.</div>';

    el('operationsIncidentCount').textContent = incidents.filter(x => !x.acknowledgedAtUtc).length;
    incidentsList.innerHTML = incidents.length ? incidents.map(i => {
      return '<div class="operations-incident"><div><strong>' + safe(i.kind) + ': ' +
        safe(i.websiteName) + '</strong><small>' + safe(new Date(i.occurredAtUtc).toLocaleString()) +
        '</small><p>' + safe(i.message) + '</p></div>' +
        (i.acknowledgedAtUtc
          ? '<span class="operations-ack">Acknowledged</span>'
          : '<button type="button" class="button secondary" data-incident-id="' +
            safe(i.id) + '">Acknowledge</button>') + '</div>';
    }).join('') : '<div class="developer-empty">No website availability incidents recorded.</div>';
  }

  async function refreshOperations() {
    if (opsBusy || appState.busy) return;
    const data = await api('/api/operations');
    renderOperations(data);
  }

  el('operationsAddForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (opsBusy || appState.busy) return;
    opsBusy = true;
    try {
      const name = el('operationsSiteName').value.trim();
      const url = el('operationsSiteUrl').value.trim();
      const response = await api('/api/operations/monitors', {
        method: 'POST', body: JSON.stringify({ name, url })
      });
      el('operationsAddForm').reset();
      notice(response.message);
    } catch (error) { notice(error.message, true); }
    finally { opsBusy = false; refreshOperations().catch(console.warn); }
  });

  opsList.addEventListener('click', async event => {
    const button = event.target.closest('button[data-monitor-id]');
    if (!button || opsBusy || appState.busy) return;
    opsBusy = true;
    try {
      const result = await api('/api/operations/monitors/' +
        encodeURIComponent(button.dataset.monitorId) + '/enabled', {
        method: 'POST', body: JSON.stringify({ enabled: button.dataset.enable === 'true' })
      });
      notice(result.message);
    } catch (error) { notice(error.message, true); }
    finally { opsBusy = false; refreshOperations().catch(console.warn); }
  });

  incidentsList.addEventListener('click', async event => {
    const button = event.target.closest('button[data-incident-id]');
    if (!button || opsBusy || appState.busy) return;
    opsBusy = true;
    try {
      await api('/api/operations/incidents/' +
        encodeURIComponent(button.dataset.incidentId) + '/acknowledge', { method: 'POST' });
      notice('Incident acknowledged.');
    } catch (error) { notice(error.message, true); }
    finally { opsBusy = false; refreshOperations().catch(console.warn); }
  });

  window.OperatorContext = {
    refresh: async () => { await Promise.all([loadPresence(), refreshOperations()]); }
  };
  window.OperatorContext.refresh().catch(error =>
    console.warn('Archer operator context could not load:', error.message));

  window.setInterval(() => {
    if (document.hidden || appState.busy) return;
    if (!el('archerView').classList.contains('hidden'))
      loadPresence().catch(console.warn);
    if (!el('operationsView').classList.contains('hidden'))
      refreshOperations().catch(console.warn);
  }, 30000);
})();